<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Log lines and the debug option

The patcher writes all of its log lines through one entry point, `PatcherLog` (`Src/RailroaderScenePatcher.Core/PatcherLog.cs`). The entry point has two modes, and the debug option selects which one a session uses.

## Modes

| Mode | Written | Lines |
| --- | --- | --- |
| Player | Always | Warnings and errors that a player or modder must see and that the game does not already show. A normal session (load, edit, save) writes none. |
| Debug | Only while the debug option is on | Load steps; the outcome of each scene patch file for each load attempt; edit results (`[Place] path: message Outstanding: ...`); save and Map Editor Undo or Redo steps; world-choice refusals (`[World choice] ...`); diagnostics such as `Group skipped: ...`, the baked scenery rule report, the frame diagnostics and the attempt traces. |

World-choice refusals also appear in the footer in both modes. A row that the game shows in its **Log** window is written once, by the game.

In the game's log file, `Player.log`, a player-mode line reads `[WRN] Scene Patcher <version>: <text>` or `[ERR] Scene Patcher <version>: <text>`. A debug line reads `[INF] Scene Patcher <version> [debug]: <text>`.

## Outputs

`PatcherLog.Output` holds the one output that a session uses. `EntryPoint.cs` sets it. The core tests set a recording output instead.

Two classes in `Src/RailroaderScenePatcher/LogOutputs.cs` are the only places that hand lines to the game:

- `ModLogOutput` writes to the game's mod logger, which tags each line with the patcher's mod. The game writes its debug level only when it starts with `-logLevelDebug`, so a debug line goes out at the information level with the `[debug]` marker. The mod logger reads text as a message template, so braces are doubled.
- `NativeRowOutput` writes the game's Log window rows (`Game.Diagnostics.MessageLog`). The game writes each row it shows to the Player log itself, so the patcher writes no second line beside it.

`PatcherLog` removes absolute paths from every line before it reaches an output. Known folders, such as the game's home folder, the game's install and data folders, and the user folder (shown as `~`), are registered with `PatcherLog.HideRoot` in `EntryPoint.cs` and replaced by a label or removed. Any other drive path, network share path or home-style path is removed by pattern, up to its file name; folder names may hold spaces, apostrophes and other punctuation. No line may carry an absolute path, because players post their logs publicly.

## Entry points

- `PatcherLog.Warning(text)` and `PatcherLog.Error(text)` write player-mode lines. With an exception argument, the player line carries the exception's base message, and the full exception goes to a debug line.
- `PatcherLog.Debug(text)` writes a debug line, only while `PatcherLog.DebugEnabled` is set.
- `PatcherLog.Debug(() => text)` does the same, but builds the text only while debug is on. Use it for text that costs work to build. A failure while the text is built drops the line.
- `LogOnce` writes a repeated failure once per key. Its `Warning`, `Error` and `Debug` methods take a key. `Clear` starts over, for example at a new load attempt.
- `LogOnChange` writes a debug diagnostic that is checked again and again, such as a frame check, the first time per key and again only when its text changes. `Changed(key, text)` answers whether to write it; `Debug(key, () => text)` writes it. `Clear` starts over.
- `BakedSceneryChecks.Summary` turns the baked scenery rule's verdicts of one load attempt into one debug block: a count per verdict, most frequent first, with the object paths only of a verdict that has at most 30 objects.
- `NativeRows` (`Src/RailroaderScenePatcher.Core/NativeRows.cs`) delivers the patcher's Log window rows once per load attempt, when the Log window is ready. A row that cannot be delivered, because the Log window fails or the load attempt or the world ends first, is written once as a player warning: `The game's Log window didn't show this message: <originator>: <message>`.
- `EditingFault` (`Src/RailroaderScenePatcher.Core/EditingFaults.cs`) holds the red line of the Scene Patcher window for one Map Editor. `Fail` takes one of the `EditingFailure` sites, such as a cancelled reload or a failed native Undo, stands in the window and is written once per text as a player error; a native Undo or Redo error reaches the log only. `Refuse` shows the refusal of an editing action, such as **Use file version**, and writes no player line: its edit feedback writes the debug line.

## Debug logging

- The option is the key `debugLogging` in `settings.json`, in the patcher's own mod folder: `{"debugLogging": true}`. The package ships no such file. The patcher creates it the first time the checkbox changes, and keeps its other values. A missing file means off. A value that is not `true` or `false` gives a player warning and leaves the option off.
- `Src/RailroaderScenePatcher.Core/DebugOption.cs` reads and writes the file through `ISettingsStore`, sets `PatcherLog.DebugEnabled`, and takes effect at once, without a restart. A session that cannot store the value keeps it for that session and gives a player error.
- `Src/RailroaderScenePatcher/PreferencesModsTab.cs` adds the checkbox **Debug logging** under the **Scene Patcher** section of the **Mods** tab in the game's Preferences, in the main menu and in the pause menu. The Mods tab is a convention that other mods share through the AppDomain data slot `ashenmare.preferences-mods.v1`. The patcher adds its section beside theirs, adds the tab itself when no other mod has, and depends on no other mod. When the game's `BuildTabs` method is missing, the option is unavailable and the log stays in player mode.

## Adding a log line

1. Decide whether the line is a warning or an error that a player or modder must see and that the game does not already show. If it is, write it with `PatcherLog.Warning` or `PatcherLog.Error`. Otherwise write it with `PatcherLog.Debug`.
2. A failure that the game's Log window shows as a row goes through `NativeRows`, not `PatcherLog`. The patcher writes no line beside that row.
3. Wrap text that costs work to build in a `Func<string>`. A diagnostic that runs every frame or on every camera step goes through `LogOnChange` or a summary, so one session's debug log stays short enough for a player to post.
4. Do not put an absolute path in a line. Use the object path or a path relative to the game's scene.
5. Do not write to `Debug.Log`, the game's message log, the mod logger or the console directly. `LogSourceTests` rejects such writes outside `LogOutputs.cs`.
6. Keep the text of `Attempt trace: ` and the JSON that follows it. `Tests/NativeObservation/Observation.cs` reads that debug line from the game's log, and a change to it breaks the observer. See [native observation](native-observation.md).

## Tests

- `Tests/RailroaderScenePatcher.Core.Tests/LogModeTests.cs` checks the player-mode lines and their wording against a recording output.
- `Tests/RailroaderScenePatcher.Core.Tests/LogSourceTests.cs` rejects direct log writes outside `LogOutputs.cs` and issue-named compile constants.
