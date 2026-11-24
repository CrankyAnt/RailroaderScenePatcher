// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// The log output that the patcher writes, as the player log receives it: one level and one text per line.
sealed class RecordingLog : ILogOutput {
    internal readonly List<KeyValuePair<LogLevel, string>> Lines = new List<KeyValuePair<LogLevel, string>>();
    public void Write(LogLevel level, string text) { Lines.Add(new KeyValuePair<LogLevel, string>(level, text)); }
    internal IEnumerable<string> At(LogLevel level) => Lines.Where(l => l.Key == level).Select(l => l.Value);
    internal IEnumerable<string> PlayerLines => Lines.Where(l => l.Key != LogLevel.Debug).Select(l => l.Key + " " + l.Value);
    // Records the patcher's log output with the debug option off or on, and restores the earlier output afterwards.
    internal static RecordingLog Capture(bool debug, Action run) {
        var log = new RecordingLog();
        var output = PatcherLog.Output; var enabled = PatcherLog.DebugEnabled;
        PatcherLog.Output = log; PatcherLog.DebugEnabled = debug;
        try { run(); } finally { PatcherLog.Output = output; PatcherLog.DebugEnabled = enabled; }
        return log;
    }
}

static class LogModeTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void Run() {
        Modes();
        DebugTextIsBuiltOnlyWithDebugOn();
        Once();
        ExceptionDetail();
        AbsolutePaths();
        ScenePatchFiles();
        UndeliveredRows();
        DebugOptionSetting();
        EditResults();
        EditingFaults();
        CullingUpdateFailure();
        Console.WriteLine("PASS: log modes");
    }

    sealed class Rows : IPatchDiagnostics {
        internal readonly List<PatchFailure> Failures = new List<PatchFailure>();
        public void Warning(PatchFailure failure) { Failures.Add(failure); }
    }
    static string Info(string name, params string[] required) =>
        "{\"name\":\"" + name + "\",\"version\":\"1.0\",\"dependencies\":{\"required\":[" + String.Join(",", required.Select(r => "\"" + r + "\"")) + "]}}";
    static string Move(string id, string path, double x, string extra = "") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x + ",0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":[]" + extra + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + items + "}}";
    const string Patcher = "crankyant/scene-patcher >=0.5.0";

    // Complete scene patch files through one load attempt: a file whose failure the Log window already shows, or that is
    // skipped for another map, gets no player line; a file-level failure without a Log window row gets exactly one warning.
    static void ScenePatchFiles() {
        var scene = new Scene(); scene.Origin = 0;
        scene.Objects["here"] = new List<object> { new Box() };
        var mods = new[] {
            LoadedMod.Read("a/applied", Info("Applied", Patcher), Doc(Move("m", "here", 5))),
            LoadedMod.Read("b/undeclared", Info("Undeclared", "other/mod >=1.0"), Doc(Move("m", "here", 5))),
            LoadedMod.Read("c/other-map", Info("Other map", Patcher), Doc(Move("m", "here", 5), "elsewhere")),
            LoadedMod.Read("d/missing-target", Info("Missing target", Patcher), Doc(Move("m", "missing", 5))),
            LoadedMod.Read("e/unreadable", Info("Unreadable", Patcher), "{broken"),
            LoadedMod.Read("f/format", Info("Format", Patcher), Doc(Move("m", "here", 5)).Replace("\"formatVersion\":1", "\"formatVersion\":2")),
            LoadedMod.Read("g/metadata", "{broken", Doc(Move("m", "here", 5))),
            LoadedMod.Read("h/unsupported", Info("Unsupported", Patcher), Doc(Move("m", "here", 5, ",\"future\":true") + "," + Move("n", "here", 6))),
        };
        var maps = scene.MapIds.ToArray();
        string[] Player(bool debug, out RecordingLog log) {
            var rows = new Rows();
            log = RecordingLog.Capture(debug, () => {
                foreach (var file in new PatchRuntime(new PatchEngine(scene), rows, "0.6.0", "game").ApplyLoaded(mods, null)) file.WriteLog(1, maps);
            });
            Assert(rows.Failures.Select(f => f.ModIdentifier).OrderBy(i => i).SequenceEqual(new[] { "c/other-map", "d/missing-target" }), "The Log window gets the other-map skip and the missing target: " + String.Join(", ", rows.Failures.Select(f => f.ModIdentifier)));
            return log.PlayerLines.ToArray();
        }
        var off = Player(false, out var offLog);
        Assert(!offLog.At(LogLevel.Debug).Any(), "Debug off writes no debug line for scene patch files");
        string Line(string id) => off.SingleOrDefault(l => l.Contains("[" + id + "]"));
        Assert(off.Length == 5, "Five files get one player warning each: " + String.Join(" | ", off));
        Assert(Line("a/applied") == null && Line("c/other-map") == null && Line("d/missing-target") == null, "An applied file, a skip for another map and a failure with a Log window row get no player line");
        Assert(Line("b/undeclared") == "Warning Undeclared [b/undeclared]: the scene patch file wasn't applied because the mod's Info.json doesn't list crankyant/scene-patcher under dependencies.required.", "A missing patcher dependency is a warning that says what to add: " + Line("b/undeclared"));
        Assert(Line("e/unreadable").StartsWith("Warning Unreadable [e/unreadable]: the scene patch file wasn't applied: Unreadable", StringComparison.Ordinal), "An unreadable file is a warning: " + Line("e/unreadable"));
        Assert(Line("f/format").StartsWith("Warning Format [f/format]: the scene patch file wasn't applied: ", StringComparison.Ordinal) && Line("f/format").Contains("format version 2"), "An unsupported format version is a warning: " + Line("f/format"));
        Assert(Line("g/metadata") == "Warning g/metadata [g/metadata]: the scene patch file wasn't applied: Cannot read scene patch file or supplying metadata: JsonReaderException.", "Unreadable metadata is a warning: " + Line("g/metadata"));
        Assert(Line("h/unsupported").StartsWith("Warning Unsupported [h/unsupported]: modification m wasn't applied because the game can't apply it in play: m: Unsupported", StringComparison.Ordinal), "A modification unsupported in play is a warning that names it: " + Line("h/unsupported"));
        var on = Player(true, out var onLog);
        Assert(on.SequenceEqual(off), "Debug on keeps the same player lines");
        var debug = onLog.At(LogLevel.Debug).Where(l => !l.StartsWith(PatchEngine.AttemptTrace, StringComparison.Ordinal)).ToArray();
        Assert(debug.Length == mods.Length && debug.All(l => l.StartsWith("load attempt 1, map map (layers layer): ", StringComparison.Ordinal)), "Debug on adds one outcome line per file: " + String.Join(" | ", debug));
        Assert(debug.Contains("load attempt 1, map map (layers layer): Applied [a/applied] scene patch file for map: 1 of 1 modifications applied, 0 failed, 0 unchanged"), "The applied file's outcome is a debug line");
        Assert(debug.Contains("load attempt 1, map map (layers layer): Other map [c/other-map] scene patch file for elsewhere skipped: not the loaded map or one of its layers"), "The other-map skip is a debug line");
    }

    static void Write() {
        PatcherLog.Debug("load step");
        PatcherLog.Warning("a warning");
        PatcherLog.Error("an error");
    }

    static void Modes() {
        var off = RecordingLog.Capture(false, Write);
        Assert(off.Lines.Count == 2 && !off.At(LogLevel.Debug).Any(), "Debug off writes only warnings and errors");
        Assert(off.At(LogLevel.Warning).Single() == "a warning" && off.At(LogLevel.Error).Single() == "an error", "Debug off keeps the warning and the error");
        var on = RecordingLog.Capture(true, Write);
        Assert(on.PlayerLines.SequenceEqual(off.PlayerLines), "Debug on keeps every player-mode line");
        Assert(on.At(LogLevel.Debug).SequenceEqual(new[] { "load step" }), "Debug on adds only the debug line");
        var silent = RecordingLog.Capture(false, () => { PatcherLog.Output = null; Write(); });
        Assert(silent.Lines.Count == 0, "Without a log output nothing is written and nothing fails");
    }

    static void DebugTextIsBuiltOnlyWithDebugOn() {
        var built = 0;
        RecordingLog.Capture(false, () => PatcherLog.Debug(() => { built++; return "costly"; }));
        Assert(built == 0, "Debug off does not build a debug line");
        var on = RecordingLog.Capture(true, () => PatcherLog.Debug(() => { built++; return "costly"; }));
        Assert(built == 1 && on.At(LogLevel.Debug).Single() == "costly", "Debug on builds and writes the debug line");
        var failing = RecordingLog.Capture(true, () => PatcherLog.Debug(() => throw new InvalidOperationException("broken")));
        Assert(failing.Lines.Count == 0, "A debug line that cannot be built is left out without a failure");
    }

    static void Once() {
        var once = new LogOnce();
        var log = RecordingLog.Capture(false, () => {
            for (var frame = 0; frame < 3; frame++) once.Warning("culling/a/broken", "Object a: broken");
            once.Warning("culling/b/broken", "Object b: broken");
            once.Clear();
            once.Warning("culling/a/broken", "Object a: broken");
        });
        Assert(log.At(LogLevel.Warning).SequenceEqual(new[] { "Object a: broken", "Object b: broken", "Object a: broken" }), "A repeated failure is written once per key until the keys clear");
    }

    static void ExceptionDetail() {
        var failure = new InvalidOperationException("outer", new System.IO.IOException("disk full"));
        var off = RecordingLog.Capture(false, () => PatcherLog.Error("The scene patch file wasn't saved", failure));
        Assert(off.Lines.Count == 1 && off.At(LogLevel.Error).Single() == "The scene patch file wasn't saved: disk full", "Debug off writes the base message without a stack trace");
        var on = RecordingLog.Capture(true, () => PatcherLog.Error("The scene patch file wasn't saved", failure));
        Assert(on.At(LogLevel.Error).Single() == "The scene patch file wasn't saved: disk full" && on.At(LogLevel.Debug).Single().Contains("System.InvalidOperationException"), "Debug on adds the full exception as a debug line");
    }

    static void AbsolutePaths() {
        var log = RecordingLog.Capture(true, () => {
            PatcherLog.HideRoot(@"C:\Users\Player\Documents\My Games\Railroader", "");
            PatcherLog.Warning(@"Could not find file 'C:\Users\Player\Documents\My Games\Railroader\Mods\Signs\Data\scene-patches.json'.");
            PatcherLog.Warning(@"Access to the path 'D:\Games\Other place\settings.json' is denied.");
            PatcherLog.Warning(@"Read failed at /home/player/.steam/Railroader/Mods/Signs/Info.json: denied");
            PatcherLog.Warning(@"Object World/Signs/Loc: Sylva Int kept its place");
            PatcherLog.Debug(@"stack at C:\build\src\EntryPoint.cs:line 12");
            PatcherLog.HideRoot(null, null);
        });
        var lines = log.Lines.Select(l => l.Value).ToArray();
        Assert(lines[0] == @"Could not find file 'Mods\Signs\Data\scene-patches.json'.", "A known root is left out: " + lines[0]);
        Assert(lines[1] == @"Access to the path 'settings.json' is denied.", "Another drive path keeps only its file name: " + lines[1]);
        Assert(lines[2] == @"Read failed at Info.json: denied", "A home path keeps only its file name: " + lines[2]);
        Assert(lines[3] == @"Object World/Signs/Loc: Sylva Int kept its place", "A scene object path is not a file path");
        Assert(lines[4] == @"stack at EntryPoint.cs:line 12", "A debug line has no absolute path either: " + lines[4]);

        var more = RecordingLog.Capture(false, () => {
            PatcherLog.HideRoot(@"C:\Users\O'Neil", "~");
            PatcherLog.Warning(@"Could not find file 'C:\Users\O'Neil\AppData\LocalLow\Stanislaw Szczepanowski\Railroader\Player.log'.");
            PatcherLog.HideRoot(null, null);
            PatcherLog.Warning(@"Could not find file 'C:\Users\O'Neil\AppData\LocalLow\Stanislaw Szczepanowski\Railroader\Player.log'.");
            PatcherLog.Warning(@"Read failed at 'D:\Games (old)\R&R, copy #2\Mods\Info.json'.");
            PatcherLog.Warning(@"Access to the path '\\server\share\Railroader\Mods\settings.json' is denied.");
            PatcherLog.Warning(@"Read failed at /Users/o'neil/Library/Application Support/Railroader/Player.log");
            PatcherLog.Warning(@"The mod's Info.json doesn't list it; object World/Signs/Loc: Sylva Int's sign stays.");
        });
        var other = more.Lines.Select(l => l.Value).ToArray();
        Assert(other[0] == @"Could not find file '~\AppData\LocalLow\Stanislaw Szczepanowski\Railroader\Player.log'.", "The user folder is left out as a known root, also with an apostrophe: " + other[0]);
        Assert(other[1] == @"Could not find file 'Player.log'.", "An unknown LocalLow path below a user folder with an apostrophe keeps only its file name: " + other[1]);
        Assert(other[2] == @"Read failed at 'Info.json'.", "Folder names with punctuation are removed: " + other[2]);
        Assert(other[3] == @"Access to the path 'settings.json' is denied.", "A UNC path keeps only its file name: " + other[3]);
        Assert(other[4] == @"Read failed at Player.log", "A macOS home path with an apostrophe and spaces keeps only its file name: " + other[4]);
        Assert(other[5] == @"The mod's Info.json doesn't list it; object World/Signs/Loc: Sylva Int's sign stays.", "Ordinary text with apostrophes stays");
    }

    // The failures of scene patch editing that the window shows in red. A patcher failure is written once as a player
    // error; a refusal of an editing action writes no player line, and its edit feedback writes its debug line.
    static void EditingFaults() {
        var expected = new Dictionary<EditingFailure, string> {
            [EditingFailure.ReloadPending] = "A working-location return is still pending.",
            [EditingFailure.ReloadStopped] = "Save failed.",
            [EditingFailure.ReloadCancelled] = "Reload cancelled: x",
            [EditingFailure.ReturnTimedOut] = "The return to your working location did not complete within five minutes. Check the game's load diagnostics before trying again.",
            [EditingFailure.LocationNotRestored] = "The Map Editor reloaded, but your working location could not be restored: x",
            [EditingFailure.OutstandingUnknown] = "Outstanding work could not be determined; the native map editor is treated as unsaved: x",
            [EditingFailure.HistoryError] = "A Map Editor Undo or Redo step failed, so scene patch editing may not match the scene: x",
            [EditingFailure.ExitCancelled] = "Exit cancelled because outstanding work could not be confirmed. x",
            [EditingFailure.FileCheck] = "Could not check the scene patch file: x",
            [EditingFailure.GizmoCleanup] = "The patcher could not verify cleanup after using the gizmo. Reload the Map Editor. x",
            [EditingFailure.HookUnavailable] = "Map Editor reload hook unavailable",
            [EditingFailure.HistoryTracking] = "Could not track a change to the Map Editor history: x",
            [EditingFailure.ChromeRefresh] = "Native map editor title and Save button could not be refreshed: x",
            [EditingFailure.HistoryCheck] = "The Map Editor history step couldn't be checked: x",
        };
        string Detail(EditingFailure site) => site == EditingFailure.ReloadPending ? "A working-location return is still pending." : site == EditingFailure.ReloadStopped ? "Save failed." : site == EditingFailure.HookUnavailable ? "Map Editor reload hook unavailable" : "x";
        Assert(Enum.GetValues(typeof(EditingFailure)).Length == expected.Count, "Every editing failure site has its expected text");
        foreach (var site in expected.Keys) {
            var fault = new EditingFault();
            var log = RecordingLog.Capture(false, () => { fault.Fail(site, Detail(site)); fault.Fail(site, Detail(site)); });
            Assert(log.Lines.Count == 1 && log.Lines[0].Key == LogLevel.Error && log.Lines[0].Value == expected[site], site + " is written once as a player error: " + String.Join(" | ", log.Lines.Select(l => l.Value)));
            Assert(fault.Text == (site == EditingFailure.HistoryError ? null : expected[site]), site + (site == EditingFailure.HistoryError ? " reaches only the log" : " stands in the window"));
        }
        var refused = new EditingFault();
        var refusal = RecordingLog.Capture(false, () => refused.Refuse("The file version can't be used: it is unreadable."));
        Assert(refusal.Lines.Count == 0 && refused.Text == "The file version can't be used: it is unreadable.", "A refusal stands in the window and writes no player line");
        var refusalDebug = RecordingLog.Capture(true, () => refused.Refuse("The file version can't be used: it is unreadable."));
        Assert(refusalDebug.Lines.Count == 0, "A refusal writes no second line beside its edit feedback");
        refused.Clear();
        Assert(refused.Text == null, "Clear empties the window line");
    }

    // The culling update failure: one text for a named group and for a failure outside any group, with the reason in
    // the player line and the full exception as a debug line.
    static void CullingUpdateFailure() {
        var failure = new InvalidOperationException("outer", new Exception("game function gone"));
        var once = new LogOnce();
        var log = RecordingLog.Capture(true, () => {
            once.Warning("k", BakedSceneryRules.CullingUpdateFailure("BushnellWhittier-MapManager/World/Mill"), failure);
            once.Warning("k", BakedSceneryRules.CullingUpdateFailure("BushnellWhittier-MapManager/World/Mill"), failure);
            once.Warning("j", BakedSceneryRules.CullingUpdateFailure(null), failure);
        });
        Assert(log.At(LogLevel.Warning).SequenceEqual(new[] {
            "Moved parts of BushnellWhittier-MapManager/World/Mill may show or hide at the wrong distance, because the patcher couldn't update their culling: game function gone",
            "Moved baked scenery parts may show or hide at the wrong distance, because the patcher couldn't update their culling: game function gone" }), "One text names the group or the parts: " + String.Join(" | ", log.At(LogLevel.Warning)));
        Assert(log.At(LogLevel.Debug).Count() == 2 && log.At(LogLevel.Debug).All(l => l.Contains("System.InvalidOperationException")), "Each written warning adds its full exception as a debug line");
    }

    // The game's Log window as the patcher reaches it: ready once the game's collector runs, and able to fail.
    sealed class Window : INativeRowOutput {
        internal bool Ready = true, Fail;
        internal readonly List<string> Rows = new List<string>();
        bool INativeRowOutput.Ready => Ready;
        public void Warning(string originator, string message, string key) { if (Fail) throw new InvalidOperationException("collector gone"); Rows.Add("Warning " + originator + ": " + message); }
        public void Notice(string originator, string message, string key) { if (Fail) throw new InvalidOperationException("collector gone"); Rows.Add("Notice " + originator + ": " + message); }
    }

    // A row the game shows in its Log window is written by the game alone. A row that can't be delivered is written once
    // in player mode instead: when the load attempt starts over, when the world ends, or when the Log window fails.
    static void UndeliveredRows() {
        var window = new Window();
        var rows = new NativeRows(window);
        var delivered = RecordingLog.Capture(true, () => { rows.Warning("w1", "mod/a", "First problem."); rows.Notice("n1", "World/Depot", "Possible incompatibility."); });
        Assert(window.Rows.SequenceEqual(new[] { "Warning mod/a: First problem.", "Notice World/Depot: Possible incompatibility." }) && !delivered.PlayerLines.Any(), "A delivered row gets no patcher line beside it");
        rows.Warning("w1", "mod/a", "First problem.");
        Assert(window.Rows.Count == 2, "A row is delivered once per load attempt");

        foreach (var ending in new[] { "re-entry", "world end" }) {
            window = new Window { Ready = false }; rows = new NativeRows(window);
            var log = RecordingLog.Capture(false, () => {
                rows.Warning("w", "mod/b", "Held problem.");
                rows.Notice("n", "World/Depot", "Held notice.");
                rows.Abandon();
                rows.Abandon();
                window.Ready = true; rows.Flush();
            });
            Assert(log.PlayerLines.SequenceEqual(new[] { "Warning The game's Log window didn't show this message: mod/b: Held problem.", "Warning The game's Log window didn't show this message: World/Depot: Held notice." }) && window.Rows.Count == 0,
                ending + ": each undelivered row is written once in player mode: " + String.Join(" | ", log.PlayerLines));
        }

        window = new Window(); rows = new NativeRows(window);
        var failed = RecordingLog.Capture(false, () => {
            window.Fail = true;
            rows.Warning("w1", "mod/c", "One.");
            rows.Warning("w2", "mod/c", "Two.");
            rows.Abandon();
        });
        Assert(failed.PlayerLines.SequenceEqual(new[] { "Warning The game's Log window didn't show this message: mod/c: One.", "Warning The game's Log window didn't show this message: mod/c: Two." }),
            "A failing Log window writes each row once in player mode: " + String.Join(" | ", failed.PlayerLines));
        var failedDebug = RecordingLog.Capture(true, () => { var again = new NativeRows(window); again.Warning("w", "mod/c", "Three."); });
        Assert(failedDebug.At(LogLevel.Debug).Any(l => l.Contains("collector gone")), "Debug on adds why the Log window failed");
    }

    // The settings file in the patcher's mod folder, as the debug option reads and writes it.
    sealed class Store : ISettingsStore {
        internal string Text; internal int Writes; internal bool FailRead;
        public string Read() { if (FailRead) throw new System.IO.IOException("locked"); return Text; }
        public void Write(string text) { Text = text; Writes++; }
    }

    static void DebugOptionSetting() {
        var enabled = PatcherLog.DebugEnabled;
        try {
            var absent = new Store();
            var option = new DebugOption(absent, available: true);
            Assert(!option.On && !PatcherLog.DebugEnabled && absent.Writes == 0, "Without a settings file the debug option is off and no file is written");
            option.Set(true);
            Assert(option.On && PatcherLog.DebugEnabled && absent.Writes == 1 && DebugOption.Read(absent.Text), "Switching the option on takes effect at once and is stored");
            var restarted = new DebugOption(new Store { Text = absent.Text }, available: true);
            Assert(restarted.On && PatcherLog.DebugEnabled, "The stored value holds after a restart");
            restarted.Set(false);
            Assert(!restarted.On && !PatcherLog.DebugEnabled, "Switching the option off stops debug lines at once");
            var other = new Store { Text = "{\"someoneElse\":3,\"debugLogging\":false}" };
            new DebugOption(other, available: true).Set(true);
            Assert(other.Text.Contains("someoneElse") && DebugOption.Read(other.Text), "Writing keeps other values in the settings file");
            foreach (var bad in new[] { "{broken", "[]", "{\"debugLogging\":\"yes\"}", "" }) {
                var log = RecordingLog.Capture(false, () => Assert(!new DebugOption(new Store { Text = bad }, available: true).On, "An unreadable value leaves the option off: " + bad));
                Assert(log.Lines.Count == 0 || log.At(LogLevel.Warning).Count() == 1, "An unreadable settings file gives at most one warning");
            }
            Assert(!new DebugOption(new Store { FailRead = true }, available: true).On, "A settings file that can't be read leaves the option off");
            // The Preferences hook is reported incompatible: the option is unavailable and the log stays in player mode,
            // whatever the settings file says; other work goes on.
            var stored = new Store { Text = "{\"debugLogging\":true}" };
            var unavailable = new DebugOption(stored, available: false);
            Assert(!unavailable.Available && !unavailable.On && !PatcherLog.DebugEnabled, "An incompatible Preferences hook leaves the debug option unavailable and off");
            unavailable.Set(true);
            Assert(!PatcherLog.DebugEnabled && stored.Writes == 0, "An unavailable option can't be switched on");
            var work = RecordingLog.Capture(PatcherLog.DebugEnabled, Write);
            Assert(work.Lines.Count == 2 && !work.At(LogLevel.Debug).Any(), "The log stays in player mode while other work writes its warnings and errors");
        } finally { PatcherLog.DebugEnabled = enabled; }
    }

    // Edit results reach the message area in both modes and the log only as debug lines.
    static void EditResults() {
        var described = 0;
        void Edit(RailroaderScenePatcher.EditFeedback feedback) {
            feedback.Report("Placement", "Placement completed.");
            feedback.Follow("Terrain output", "Terrain output completed.");
            feedback.Refuse("Track binding", "Track binding", "The segment doesn't exist.");
        }
        RailroaderScenePatcher.EditFeedback Feedback() => new RailroaderScenePatcher.EditFeedback { Describe = text => { described++; return "[x] " + text; } };
        var offFeedback = Feedback();
        var off = RecordingLog.Capture(false, () => Edit(offFeedback));
        Assert(off.Lines.Count == 0 && described == 0 && offFeedback.Messages.Count != 0, "Debug off logs no edit result and builds no log line; the message area still has the result");
        var on = RecordingLog.Capture(true, () => Edit(Feedback()));
        Assert(on.Lines.All(l => l.Key == LogLevel.Debug) && on.Lines.Count == 3 && on.Lines[0].Value == "[x] Placement completed.", "Debug on logs each edit result as a debug line: " + String.Join(" | ", on.Lines.Select(l => l.Value)));
    }
}
