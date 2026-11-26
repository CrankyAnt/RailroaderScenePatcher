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
        OnChange();
        OnChangeSharedPath();
        ExceptionDetail();
        AbsolutePaths();
        ScenePatchFiles();
        UndeliveredRows();
        DebugOptionSetting();
        EditResults();
        WindowGuidanceStaysInTheWindow();
        GuidanceLeavesTheLogLine();
        AttemptIdentifiers();
        LinesOfOneLoad();
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
        var lastRows = new Rows();
        string[] Player(bool debug, out RecordingLog log) {
            var rows = lastRows = new Rows();
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
        Assert(Line("b/undeclared") == "Warning Undeclared [b/undeclared]: Data/scene-patches.json wasn't applied because Info.json doesn't list crankyant/scene-patcher under dependencies.required. Report this to the mod author.", "A missing patcher dependency is a warning that names the missing dependency: " + Line("b/undeclared"));
        Assert(Line("e/unreadable").StartsWith("Warning Unreadable [e/unreadable]: Data/scene-patches.json wasn't applied: Unreadable", StringComparison.Ordinal), "An unreadable file is a warning: " + Line("e/unreadable"));
        Assert(Line("f/format").StartsWith("Warning Format [f/format]: Data/scene-patches.json wasn't applied: ", StringComparison.Ordinal) && Line("f/format").Contains("format version 2"), "An unsupported format version is a warning: " + Line("f/format"));
        Assert(Line("g/metadata") == "Warning g/metadata [g/metadata]: Data/scene-patches.json wasn't applied: Cannot read scene patch file or supplying metadata: JsonReaderException. Report this to the mod author.", "Unreadable metadata is a warning: " + Line("g/metadata"));
        Assert(Line("h/unsupported").StartsWith("Warning Unsupported [h/unsupported]: Data/scene-patches.json modification m wasn't applied because the game can't apply it in play: m: Unsupported", StringComparison.Ordinal), "A modification unsupported in play is a warning that names it: " + Line("h/unsupported"));
        // The Log window row of a target that is not in the loaded map says what is missing and what the modder can do.
        var missing = lastRows.Failures.Single(f => f.ModIdentifier == "d/missing-target").Message;
        Assert(missing.Contains("This target doesn't match any object in the loaded map. ") && !missing.Contains("resolve uniquely"),
            "A missing target reads in plain words: " + missing);
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

    // A diagnostic that is checked every frame, such as a frame check: written per key the first time and again only when its
    // text changes, built only while debug is on.
    static void OnChange() {
        var change = new LogOnChange(); var built = 0;
        Func<string> same = () => { built++; return "Frame check for Heap. Distance: 12 m"; };
        var off = RecordingLog.Capture(false, () => change.Debug("heap", same));
        Assert(off.Lines.Count == 0 && built == 0, "Debug off builds and writes no frame check");
        var log = RecordingLog.Capture(true, () => {
            for (var frame = 0; frame < 3; frame++) change.Debug("heap", same);
            change.Debug("dock", () => "Frame check for Dock. Distance: 30 m");
            change.Debug("heap", () => "Frame check for Heap. Distance: 40 m");
            change.Debug("heap", () => "Frame check for Heap. Distance: 40 m");
            change.Clear();
            change.Debug("heap", () => "Frame check for Heap. Distance: 40 m");
        });
        Assert(log.At(LogLevel.Debug).SequenceEqual(new[] { "Frame check for Heap. Distance: 12 m", "Frame check for Dock. Distance: 30 m", "Frame check for Heap. Distance: 40 m", "Frame check for Heap. Distance: 40 m" }),
            "A frame check is written once per object and again only when it changes, until the keys clear");
    }

    // Three crossing pads share one path and one label, as at Old River Rd: each keeps its own entry by its identity, so
    // checking them in turn every frame writes each block once instead of rewriting it as the key flips between them.
    static void OnChangeSharedPath() {
        var change = new LogOnChange();
        const string path = "World/Roads Bryson/Old River Rd/Crossing Unprotected/Crossing Pad";
        var log = RecordingLog.Capture(true, () => {
            for (var frame = 0; frame < 26; frame++)
                foreach (var pad in new[] { 101, 202, 303 })
                    change.Debug(LogOnChange.ObjectKey(path, "Crossing pad", pad), () => "Frame check for Crossing pad " + pad);
        });
        Assert(log.At(LogLevel.Debug).SequenceEqual(new[] { "Frame check for Crossing pad 101", "Frame check for Crossing pad 202", "Frame check for Crossing pad 303" }),
            "Objects with one path and label are written once each: " + log.Lines.Count + " lines");
    }

    static void ExceptionDetail() {
        var failure = new InvalidOperationException("outer", new System.IO.IOException("disk full"));
        var off = RecordingLog.Capture(false, () => PatcherLog.Error("The scene patch file wasn't saved", failure));
        Assert(off.Lines.Count == 1 && off.At(LogLevel.Error).Single() == "The scene patch file wasn't saved: disk full", "Debug off writes the base message without a stack trace");
        var on = RecordingLog.Capture(true, () => PatcherLog.Error("The scene patch file wasn't saved", failure));
        Assert(on.At(LogLevel.Error).Single() == "The scene patch file wasn't saved: disk full" && on.At(LogLevel.Debug).Single().Contains("System.InvalidOperationException"), "Debug on adds the full exception as a debug line");
        // A sentence that ends with whom to report to takes the message after a colon, without a period before it.
        var report = RecordingLog.Capture(true, () => PatcherLog.Warning("The patcher couldn't add the Mods tab to Preferences. Report this to the patcher maintainer.", failure));
        Assert(report.At(LogLevel.Warning).Single() == "The patcher couldn't add the Mods tab to Preferences. Report this to the patcher maintainer: disk full",
            "The exception message follows the report sentence after one colon: " + report.At(LogLevel.Warning).Single());
        var detail = report.At(LogLevel.Debug).Single();
        Assert(detail.StartsWith(PatcherLog.ExceptionDetail, StringComparison.Ordinal) && detail.IndexOf('\n') < 0 && detail.IndexOf('\r') < 0 && detail.Contains(" | "),
            "The full exception is one marked debug line: " + detail);
        // A text with line breaks is written as one physical line.
        var joined = RecordingLog.Capture(true, () => { PatcherLog.Debug("first\r\nsecond\nthird"); PatcherLog.Warning("one\ntwo"); });
        Assert(joined.Lines.Select(l => l.Value).SequenceEqual(new[] { "first | second | third", "one | two" }), "Every entry is one physical line: " + String.Join(" / ", joined.Lines.Select(l => l.Value)));
        // A scene patch file line names whom to report to once: the patcher maintainer without the mod author, or the
        // mod author by name; never a period after the reason's own period.
        var source = new PatchSource("example/mod", "Example Mod", "2.1", "Example author", "https://example.com/mod");
        Assert(ScenePatchFileReport.Line(source, "Spawn point placement failed before the new game start: denied.", "0.5.0", toMaintainer: true)
            == "Example Mod [example/mod 2.1] Data/scene-patches.json: Spawn point placement failed before the new game start: denied. Patcher 0.5.0. Report this to the patcher maintainer.",
            "A patcher failure names the maintainer once: " + ScenePatchFileReport.Line(source, "Spawn point placement failed before the new game start: denied.", "0.5.0", toMaintainer: true));
        Assert(ScenePatchFileReport.Line(source, "The file isn't valid JSON", "0.5.0", toMaintainer: false)
            == "Example Mod [example/mod 2.1] Data/scene-patches.json: The file isn't valid JSON. Patcher 0.5.0. Mod author: Example author; website: https://example.com/mod",
            "A diagnostic of the mod's file names its author: " + ScenePatchFileReport.Line(source, "The file isn't valid JSON", "0.5.0", toMaintainer: false));
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
            [EditingFailure.ReturnTimedOut] = "The return to your working location timed out after five minutes. Check the Player log for loading errors before trying again.",
            [EditingFailure.LocationNotRestored] = "The Map Editor reloaded, but your working location could not be restored: x",
            [EditingFailure.OutstandingUnknown] = "The patcher couldn't check for unsaved changes. It treats the Map Editor as unsaved: x",
            [EditingFailure.HistoryError] = "A Map Editor Undo or Redo step failed, so scene patch editing may not match the scene: x",
            [EditingFailure.ExitCancelled] = "The Map Editor stayed open because the patcher couldn't check for unsaved changes. x",
            [EditingFailure.FileCheck] = "Could not check the scene patch file: x",
            [EditingFailure.GizmoCleanup] = "The patcher couldn't confirm removal of the gizmo's temporary changes. Reload the Map Editor. x",
            [EditingFailure.GizmoRestore] = "x",
            [EditingFailure.GizmoPreview] = "The gizmo drag failed: x",
            [EditingFailure.GizmoUnavailable] = "The gizmo couldn't be shown: x",
            [EditingFailure.HookUnavailable] = "Map Editor reload hook unavailable",
            [EditingFailure.HistoryTracking] = "The patcher couldn't track an Undo or Redo history change in the Map Editor: x",
            [EditingFailure.ChromeRefresh] = "The Map Editor title and Save button couldn't be updated: x",
            [EditingFailure.HistoryCheck] = "The Map Editor Undo or Redo step couldn't be checked: x",
        };
        // The window shows the repair step; the log line leaves it out.
        var logged = new Dictionary<EditingFailure, string> {
            [EditingFailure.ReturnTimedOut] = "The return to your working location timed out after five minutes.",
            [EditingFailure.GizmoCleanup] = "The patcher couldn't confirm removal of the gizmo's temporary changes. x",
        };
        string Detail(EditingFailure site) => site == EditingFailure.ReloadPending ? "A working-location return is still pending." : site == EditingFailure.ReloadStopped ? "Save failed." : site == EditingFailure.HookUnavailable ? "Map Editor reload hook unavailable" : "x";
        Assert(Enum.GetValues(typeof(EditingFailure)).Length == expected.Count, "Every editing failure site has its expected text");
        foreach (var site in expected.Keys) {
            var fault = new EditingFault();
            var log = RecordingLog.Capture(false, () => { fault.Fail(site, Detail(site)); fault.Fail(site, Detail(site)); });
            var sentence = logged.TryGetValue(site, out var line) ? line : expected[site];
            if (!sentence.EndsWith(".", StringComparison.Ordinal)) sentence += ".";
            Assert(log.Lines.Count == 1 && log.Lines[0].Key == LogLevel.Error && log.Lines[0].Value == sentence + " Report this to the patcher maintainer.", site + " is written once as a player error: " + String.Join(" | ", log.Lines.Select(l => l.Value)));
            Assert(fault.Text == (site == EditingFailure.HistoryError ? null : expected[site]), site + (site == EditingFailure.HistoryError ? " reaches only the log" : " stands in the window"));
        }
        // A failure about the scene patch file or an object names it in the log line; the window keeps its text.
        var named = new EditingFault();
        var namedLog = RecordingLog.Capture(false, () => {
            named.Fail(EditingFailure.FileCheck, "denied", "[crankyant/maptest] scene patch file");
            named.Fail(EditingFailure.GizmoCleanup, "hook gone", "World/Large Scenery/Bryson/Bryson Coal Dealer");
        });
        Assert(namedLog.Lines.Select(l => l.Value).SequenceEqual(new[] {
            "[crankyant/maptest] scene patch file: Could not check the scene patch file: denied. Report this to the patcher maintainer.",
            "World/Large Scenery/Bryson/Bryson Coal Dealer: The patcher couldn't confirm removal of the gizmo's temporary changes. hook gone. Report this to the patcher maintainer." }),
            "Each line names its file or object: " + String.Join(" | ", namedLog.Lines.Select(l => l.Value)));
        Assert(named.Text == "The patcher couldn't confirm removal of the gizmo's temporary changes. Reload the Map Editor. hook gone", "The window keeps its own text: " + named.Text);
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
            once.Warning("k", SceneObjectRules.CullingUpdateFailure("BushnellWhittier-MapManager/World/Mill"), failure);
            once.Warning("k", SceneObjectRules.CullingUpdateFailure("BushnellWhittier-MapManager/World/Mill"), failure);
            once.Warning("j", SceneObjectRules.CullingUpdateFailure(null), failure);
        });
        Assert(log.At(LogLevel.Warning).SequenceEqual(new[] {
            "The patcher couldn't update visibility for moved parts of BushnellWhittier-MapManager/World/Mill. Report this to the patcher maintainer: game function gone",
            "The patcher couldn't update visibility for moved parts. Report this to the patcher maintainer: game function gone" }), "One text names the group or the parts: " + String.Join(" | ", log.At(LogLevel.Warning)));
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
            Assert(log.PlayerLines.SequenceEqual(new[] { "Warning Log window message delivery failed or the Log window wasn't ready: mod/b: Held problem. Report delivery failures to the patcher maintainer.", "Warning Log window message delivery failed or the Log window wasn't ready: World/Depot: Held notice. Report delivery failures to the patcher maintainer." }) && window.Rows.Count == 0,
                ending + ": each undelivered row is written once in player mode: " + String.Join(" | ", log.PlayerLines));
        }

        window = new Window(); rows = new NativeRows(window);
        var failed = RecordingLog.Capture(false, () => {
            window.Fail = true;
            rows.Warning("w1", "mod/c", "One.");
            rows.Warning("w2", "mod/c", "Two.");
            rows.Abandon();
        });
        Assert(failed.PlayerLines.SequenceEqual(new[] { "Warning Log window message delivery failed or the Log window wasn't ready: mod/c: One. Report delivery failures to the patcher maintainer.", "Warning Log window message delivery failed or the Log window wasn't ready: mod/c: Two. Report delivery failures to the patcher maintainer." }),
            "A failing Log window writes each row once in player mode: " + String.Join(" | ", failed.PlayerLines));
        var failedDebug = RecordingLog.Capture(true, () => { var again = new NativeRows(window); again.Warning("w", "mod/c", "Three."); });
        Assert(failedDebug.At(LogLevel.Debug).Any(l => l.Contains("collector gone")), "Debug on adds why the Log window failed");
        Assert(failedDebug.At(LogLevel.Debug).Any(l => l.StartsWith("Log window message delivery failed for row w from mod/c: ", StringComparison.Ordinal)),
            "The delivery failure names the row key and originator that the row's other lines carry: " + String.Join(" | ", failedDebug.At(LogLevel.Debug)));

        RowsKeepTheirRepairStep();
    }
    // In play mode the Scene Patcher window is not open, so a Log window row for a patcher failure names its repair step.
    // A row that can't be delivered is written once to the Player log without it, with the attempt number of the load
    // the row was raised in, even when the next load has begun.
    static void RowsKeepTheirRepairStep() {
        var window = new Window();
        var rows = new NativeRows(window);
        RecordingLog.Capture(true, () => rows.Warning("g", "mod/d", "The bridge differs. " + WindowGuidance.ReloadBeforeEditing));
        Assert(window.Rows.Single() == "Warning mod/d: The bridge differs. Reload before editing it.", "A Log window row names its repair step: " + window.Rows.Single());

        LoadAttempt.Reset();
        try {
            window = new Window { Ready = false }; rows = new NativeRows(window);
            LoadAttempt.Begin("rr/bushnell-whittier");
            rows.Warning("h", "mod/e", "The bridge differs. " + WindowGuidance.ReloadBeforeEditing);
            LoadAttempt.Begin("rr/bushnell-whittier");
            var held = RecordingLog.Capture(false, () => rows.Abandon());
            Assert(held.PlayerLines.Single() == "Warning load attempt 1, map rr/bushnell-whittier: Log window message delivery failed or the Log window wasn't ready: mod/e: The bridge differs. Report delivery failures to the patcher maintainer.",
                "The undelivered row is written without its repair step, with the attempt number of its own load: " + String.Join(" | ", held.PlayerLines));
        } finally { LoadAttempt.Reset(); }
    }
    // While a load is in progress every line starts with its identifiers, also from parts that know nothing of the load;
    // a line that already names them, an attempt trace and a line outside a load stay as they are.
    static void LinesOfOneLoad() {
        LoadAttempt.Reset();
        try {
            var log = RecordingLog.Capture(true, () => {
                PatcherLog.Debug("Before any load.");
                LoadAttempt.Begin("map");
                PatcherLog.Warning("A warning.");
                PatcherLog.Debug(LoadedScenePatchFile.AttemptLabel(1, new[] { "map" }) + ": a labelled line.");
                PatcherLog.Debug(PatchEngine.AttemptTrace + "{}");
                LoadAttempt.End();
                PatcherLog.Debug("After the load.");
                LoadAttempt.Resume();
                PatcherLog.Debug("Another callback.");
                LoadAttempt.End();
            });
            Assert(log.Lines.Select(l => l.Value).SequenceEqual(new[] { "Before any load.", "load attempt 1, map map: A warning.", "load attempt 1, map map: a labelled line.", PatchEngine.AttemptTrace + "{}", "After the load.", "load attempt 1, map map: Another callback." }),
                "Lines of one load carry its attempt number: " + String.Join(" | ", log.Lines.Select(l => l.Value)));

            var opened = 0;
            var binding = new LocalWorldBinding<object>(() => new object(), _ => { }, _ => PatcherLog.Debug("Reconciled."), () => { opened++; LoadAttempt.Begin("map"); });
            var reentry = RecordingLog.Capture(true, () => {
                binding.Enter("world", 1); binding.Enter("world", 1);
                binding.EndAttempt(); binding.Enter("world", 2);
            });
            Assert(opened == 2 && LoadAttempt.Number == 3 && reentry.Lines.Single().Value == "load attempt 3, map map: Reconciled.",
                "Each opened load attempt begins a load before the retained state is reconciled: " + opened + " / " + String.Join(" | ", reentry.Lines.Select(l => l.Value)));
        } finally { LoadAttempt.Reset(); }
    }
    // Every debug line of one load attempt starts with the same identifiers: the attempt number and the loaded map.
    static void AttemptIdentifiers() {
        Assert(LoadedScenePatchFile.AttemptLabel(3, new[] { "rr/bushnell-whittier", "layer" }) == "load attempt 3, map rr/bushnell-whittier", "The label names the attempt and the loaded map");
        Assert(LoadedScenePatchFile.AttemptLabel(1, null) == "load attempt 1, map unknown" && LoadedScenePatchFile.AttemptLabel(1, new string[0]) == "load attempt 1, map unknown", "A map that can't be read is unknown");
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
        // A result of an action on the scene patch file, such as Save, names the file; a result of an edit names the inspected object.
        var subject = new RailroaderScenePatcher.EditFeedback();
        subject.FilePlaces.Add("Save");
        subject.Describe = text => subject.LogLine(text, "World/Roads Sylva/Grade Crossing - Allen", "[crankyant/maptest] scene patch file");
        var named = RecordingLog.Capture(true, () => {
            subject.Refuse("Save", "Save", "An import is open in Scene Patcher.");
            subject.Report("Placement", "Placement completed.");
        });
        Assert(named.Lines.Select(l => l.Value).SequenceEqual(new[] {
            "[Save] [crankyant/maptest] scene patch file: Save refused. An import is open in Scene Patcher.",
            "[Placement] World/Roads Sylva/Grade Crossing - Allen: Placement completed." }),
            "A Save line names the scene patch file and an edit line names its object: " + String.Join(" | ", named.Lines.Select(l => l.Value)));
    }
    // A refusal in the Scene Patcher window keeps the repair step its reason carries; the debug line of the same result
    // records the refusal without it.
    static void WindowGuidanceStaysInTheWindow() {
        var scene = new Scene(); var box = new Box { X = 7 };
        scene.Objects["p"] = new List<object> { box };
        scene.Observations = () => new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var feedback = new RailroaderScenePatcher.EditFeedback();
        box.X = 8;
        var stale = RecordingLog.Capture(true, () => feedback.Run("placement", "Placement", null, inventory, inventory.Entries.Single(), _ => null));
        Assert(feedback.Result == "Placement refused. The inventory is stale (p changed (position)). Refresh and inspect again.",
            "The window shows the stale refusal with its repair step: " + feedback.Result);
        Assert(stale.Lines.Count == 1 && stale.Lines[0].Key == LogLevel.Debug && stale.Lines[0].Value == "Placement refused. The inventory is stale (p changed (position)).",
            "The debug line records the refusal without the repair step: " + String.Join(" | ", stale.Lines.Select(l => l.Value)));

        var nothing = RecordingLog.Capture(true, () => feedback.Run("placement", "Placement", null, null, null, _ => null));
        Assert(feedback.Result == "Placement refused. No object is inspected. Refresh the inventory and inspect the object first.",
            "The window says how to inspect an object: " + feedback.Result);
        Assert(nothing.Lines.Single().Value == "Placement refused. No object is inspected.",
            "The debug line names only the refusal: " + nothing.Lines.Single().Value);

        var fault = new EditingFault();
        var timedOut = RecordingLog.Capture(false, () => fault.Fail(EditingFailure.ReturnTimedOut, "x"));
        Assert(fault.Text.EndsWith(" " + WindowGuidance.CheckPlayerLog, StringComparison.Ordinal) && timedOut.Lines.Single().Value == "The return to your working location timed out after five minutes. Report this to the patcher maintainer.",
            "An editing error keeps its repair step in the window, and its player line names the report recipient instead: " + fault.Text + " / " + timedOut.Lines.Single().Value);
    }
    // PatcherLog leaves every window guidance text out of a line, wherever the reason put it, and keeps the rest of the
    // line; an attempt trace keeps its payload as the engine recorded it.
    static void GuidanceLeavesTheLogLine() {
        foreach (var guidance in WindowGuidance.All) {
            Assert(guidance.EndsWith(".", StringComparison.Ordinal) && guidance.IndexOf('"') < 0 && guidance.IndexOf('\\') < 0, "A guidance text is whole sentences without quotes: " + guidance);
            Assert(WindowGuidance.Without("The reason. " + guidance) == "The reason.", "A guidance text at the end leaves the line: " + guidance);
            Assert(WindowGuidance.Without("The reason: x. " + guidance.TrimEnd('.')) == "The reason: x.", "A guidance text without its full stop leaves the line: " + guidance);
            Assert(WindowGuidance.Without("The reason. " + guidance + " What remains: y.") == "The reason. What remains: y.", "A guidance text in the middle leaves the line: " + guidance);
        }
        Assert(WindowGuidance.Without(WindowGuidance.SelectAgain + " Snap target changed.") == "Snap target changed.", "A guidance text that starts the line leaves it with its space");
        Assert(WindowGuidance.Without("Reload before editing it later.") == "Reload before editing it later.", "A sentence that only starts like a guidance text stays");
        Assert(WindowGuidance.Without("Snap target changed (" + WindowGuidance.SelectAgain + ")") == "Snap target changed (" + WindowGuidance.SelectAgain + ")", "A guidance text inside parentheses stays");
        var trace = PatchEngine.AttemptTrace + "{\"error\":\"x. " + WindowGuidance.ReloadBeforeEditing + "\"}";
        var log = RecordingLog.Capture(true, () => { PatcherLog.Debug(trace); PatcherLog.Warning("The bridge differs. " + WindowGuidance.ReloadBeforeEditing); });
        Assert(log.Lines[0].Value == trace && log.Lines[1].Value == "The bridge differs.", "The trace keeps its payload; a warning loses its guidance: " + String.Join(" | ", log.Lines.Select(l => l.Value)));
    }
}
