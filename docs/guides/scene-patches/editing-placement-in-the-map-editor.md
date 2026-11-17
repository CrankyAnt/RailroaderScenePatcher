<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Edit and save fixed placement in the native map editor

This guide shows how to move, rotate, disable and restore a supported scene object in the native map editor, and how Save, Undo and Redo treat those edits. Editing currently supports [independent boards](moving-boards.md), [the Ela bridge](placing-bridges.md), and [baked scenery groups and their parts](moving-baked-scenery-groups.md). The automated tests cover the behavior below with controlled scene, history and storage dependencies. A recorded journey in Railroader for these editing steps is not yet part of this guide.

## Before you start

Open your mod's map definition from the Editor menu. The patcher reads and writes `Data/scene-patches.json` and `Info.json` only in the mod you are editing. It does not read or write those files in a mod that supplies a map layer or terrain tile set. Save creates the scene patch file and declares the dependency if needed.

A new scene patch file names your map definition's bottom map layer as its scene target map, or the definition itself if it has no map layers. If the bottom map layer is the base-game map, the file also applies to a save of that map, where your map-changing script or map patch brings your native changes. An existing scene patch file keeps its scene target map. See [the scene patch file reference](scene-patch-file-v1.md#scene-patch-file-fields).

Open **Scene patches** and click the page icon to open its session details. The **Editing** section reads **Editing ready for** followed by the scene patch file path. If it reports a problem instead, the file is protected. The patcher never overwrites an unreadable scene patch file, a file in a newer format, or one for another map. The **Scene patch file** section shows where the file is wrong. You can correct the file and read it again, or start fresh behind a backup. See [Recover a protected scene patch file](recover-scene-patch-files.md).

## Choose a scene patch file version

The patcher checks `Data/scene-patches.json` when the game regains focus and when you click **Refresh** or **Save**. It compares content rather than formatting. Changes to whitespace, object-member order or the order of entries with stable identities do not create a version choice.

When the file content changes outside the editing session, Scene patches says `The scene patch file changed outside scene patch editing. Save won't overwrite it until you choose a version.` The Map Editor's **Save** button remains enabled, but clicking it writes nothing until you choose a version. Scene patches shows `Save refused.` and the reason in its message area. If Scene patches is closed, a **Save refused** alert shows the reason instead. Choose one of these versions:

- **Use file version** applies the changed file in the current session and discards unsaved scene patch edits. Scene patches explains this loss before you confirm. The discarded history step remains in native Undo and Redo under its original action name, and the menu titles still identify it, but the step has no effect. Undo or Redo of that step, or of a scene patch edit recorded before the choice, does not change the scene patch file. Unsaved native map work is unaffected. Afterward, the scene patch status is **Saved** if the session's scene patch content equals the file on disk, and **Unsaved** otherwise.
- **Keep editor version** retains the session's scene patch file and edits. Scene patches says that the next Save will overwrite the changed file.

Neither choice requires a reload.

The choice remains visible after the action that detected the change. Undo, Redo and **Remove modification** update reload feedback to match the current session. If the file changed while the game was closed, the next native map editor load reads it without a version-choice dialog.

If the file cannot be read as a valid scene patch file, or its format version is missing, unrecognized or newer, Scene patches enters **File needs repair** and shows the diagnosis with line numbers. **Use file version** is unavailable. Scene patch controls are disabled except **Refresh**, **Keep editor version**, **Details** and **Copy to clipboard**. Clicking **Save** writes nothing and shows `Save refused.` with the diagnosis in the message area. See [Recover a protected scene patch file](recover-scene-patch-files.md).

If a repair check finds that the file is readable but still changed, Scene patches says `The file is readable again.` and offers the normal version choice. If the file's content is unchanged, the choice clears.

## Set a numeric placement

For a step-by-step edit that keeps the same object selected and lets you review changes before **Apply**, see [complete an edit with a guided task](completing-guided-tasks.md).

1. Find the object in the [outliner](inspect-scene-objects.md) or [select it in the world](select-world-targets.md), then confirm its identity.
2. On the **Placement** tab, use **Numeric** mode to change the values. Use a decimal point.
3. Click **Apply**.

For an object that supports fixed placement, the six fields show its inspected position in map coordinates and rotation in degrees. The **Position (m)** and **Rotation (deg)** rows each have **X**, **Y** and **Z** in front of their boxes. Other objects cannot be edited. They show the same values as two lines of text, such as `X 4361.091  Y 528.405  Z 5380.431 (m)`, and the amber **Editing** message gives the object's reason.

Numbers are displayed to three decimals, never in scientific notation. An unchanged field still represents the object's exact value, so changing X alone changes neither the other axes nor the rotation. **Apply** is inactive while the fields hold the current placement. **Supported edits** on the **Object** tab explains why **Apply** is unavailable. It becomes available as soon as a keystroke makes a value differ, without Enter or leaving the field, and becomes inactive again if the value equals the current one. Applying a placement equal to the object's placement without this mod removes the placement from the modification.

When the edit runs, the object moves and the scene patch file receives one modification for it. The modification holds the fixed position and rotation as a quaternion. An axis whose value the game stores unchanged keeps its text in the scene patch file. For example, changing Z alone does not rewrite X as a longer number read back from the game. Applying again replaces the values without adding a second modification or accumulating movement. **Read** refills the fields from the inspected observation.

Every edit is tied to that observation. The patcher refuses it if the object, its placement, activity, bindings or dependencies no longer match what you inspected, or if the inventory is stale. The first message slot shows the action group with **Working...** until the edit has run, then shows the result, such as `Placement completed.` A completed edit refreshes the inventory a moment later. After Undo, Redo or a native edit, click **Refresh** before the next edit. If you forget, the refusal tells you to refresh.

If an edit is refused or does not run, the message says why. The message area lists the newest message on top and dims older messages below it. Each starts with its action group. Only the latest automatic follow-up message, such as **Terrain output**, is kept, so these messages do not push the action result out of the area. Scroll the area if the messages do not fit. Inspecting another object clears all messages.

Above the messages, the first line shows why the inspected object cannot currently be edited under **Editing**, or which world choice, gizmo or prepared snap is pending. An action that cannot run stays visible and disabled; its tooltip gives the reason. The reason also goes to the log. Reasons include a stale inventory and what made it stale, values that cannot be read, and an edit requested while a world selection or gizmo is pending.

## Disable or enable the object

On the **Object** tab, click **Disable** or **Enable**. The scene patch file stores the choice as `"active": false` or `"active": true` in the same modification as the placement. Disabling a board makes the game release its culling and terrain mask. A disabled bridge stops colliding. Generic activity requires patcher 0.2.0; native board, bridge and track-marker activity capabilities require 0.5.0. Save derives the dependency minimum from the scene patch file's content. See [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

After a board edit, the **Terrain** section of the **Changes** tab shows the state of the terrain output the game regenerates at the old and new positions. Save does not write that generated output; see [move a board with its terrain effects](moving-boards.md) for its completion, retry and limits.

## Undo and redo

Each completed edit is one entry in the native map editor's own history, between your native edits. Use the native map editor's **Undo** and **Redo**. They reverse and reapply the object's state and the scene patch file together.

Reloading the native map editor ends that history. The patcher does not carry Undo or Redo across a reload.

If the native map editor's Undo or Redo itself stops on an error, the session details show **Incomplete operation** and the native map editor's history can no longer be trusted to record edits. Save still writes the current state, but the title star, the Save button's tint and tooltip, and the exit confirmation keep reporting unsaved native map data until you reload the native map editor.

If a patcher edit cannot be reversed or reapplied, the session details also show **Incomplete operation**. The scene patch file keeps the state the panel shows, the native map editor stays unsaved, and Save remains available.

## Remove a modification

Click **Remove modification** to delete this mod's modification of the selected object, whether saved or unsaved. The object returns to its effective underlying state: the state the loaded map gives it, with the remaining modifications in your scene patch file still applied. It does not return to an earlier position from a scene patch. Undo restores the modification and Save writes the removal.

If another modification in your scene patch file names this one in `dependsOn`, removal is refused and the message names those modifications. Change or remove them first. The same holds for **Clear activity** and any other edit that would leave the modification empty.

The native map editor preserves a modification containing content this patcher version does not understand. You cannot edit or remove it there.

You cannot edit a modification that does not take effect because a dependency is missing, fails or forms a cycle. The refusal gives the reason. You can remove it if no other modification names it in `dependsOn`. In a cycle of two or more modifications, each is named by another, so none can be removed here. Break the cycle in the file and reopen the native map editor. If a manually written scene patch file has several modifications for the same object, combine them in the file first.

## Save

Use the native map editor's **Save**. One Save covers three writes for the edited mod:

- the native map data,
- `Data/scene-patches.json`,
- the patcher dependency in `Info.json`.

Save adds or raises `crankyant/scene-patcher >=<minimum>` to the highest minimum required by the scene patch file's content. Legacy fixed placement needs `0.1.0`, generic activity needs `0.2.0`, and newer capabilities retain their `0.5.0` requirements; see the [dependency reference](scene-patch-file-v1.md#dependency-minimum). A constraint that already requires the necessary version or a later one stays unchanged, and so does an upper bound. Save writes each patcher file only when its content differs. Save never reloads the native map editor.

The title star and the Save button's orange tint and **Unsaved Changes** tooltip reflect all three writes. They appear after Undo, Redo or a new edit whenever the current state differs from the last complete Save. This includes edits that change only the scene patch file and a different history with the same number of changes. The Save button is always enabled. If nothing is unsaved, Save writes no patcher file.

## Retry after a failed Save

A refused Save writes nothing, for example while a version choice is pending. Scene patches shows the reason in its message area. If the window is closed, a **Save refused** alert shows the reason instead.

If a write fails, the native map editor shows **Save Failed** with the cause and the completed and outstanding writes. The session details repeat the report as **Last Save failed**. Completed writes are not reversed. Your edits stay in memory and the native map editor stays unsaved.

A failed write never damages the file it was replacing. The patcher first writes the complete new content of `Data/scene-patches.json` or `Info.json` to a file beside it, ending in `.saving`, and then puts it in the file's place. If the write stops midway, the previous file remains unchanged, or there is no file if this was the first Save. The patcher removes the unfinished `.saving` file. If an interrupted Save, for example after a crash, leaves a `.saving` file, the next Save replaces it. If another program, such as a virus scanner or sync client, briefly holds the file open, Save retries for up to half a second before reporting the failure.

Correct the cause, such as a locked or read-only file, and click **Save** again. The retry writes only what is outstanding. If `Data/scene-patches.json` changed outside the editing session after the patcher read it, the retry refuses to overwrite it. Choose **Use file version** or **Keep editor version** before retrying. A file that needs repair requires a backup first.

Save refuses to write the dependency when an existing constraint in `Info.json`, such as `^0.1.0`, excludes the version the scene patch file needs. Correct that constraint and save again.

## Retry after a failed edit

The message area distinguishes three outcomes. Each message describes the problem and how to correct it.

- `refused` means the patcher stopped before changing anything. The scene, scene patch file and history are unchanged. Correct the value or selection named in the message and apply again. If the message says the object changed or the inventory is stale, click **Refresh**, inspect the object again and repeat the action.
- `failed and was undone` means the change started but failed. The patcher restored the object and confirmed the restoration. The scene patch file is unchanged and the history has no new step. Correct the cause and apply again. For a board, the message reads `was undone; waiting for terrain` instead. The object is restored, but the game is still regenerating the terrain at its restored placement. An edit before the terrain is updated gets the same message. Once the terrain is updated, edit again or click **Retry** on the **Changes** tab. Recheck is not needed.
- `failed; cleanup is incomplete` means the patcher could not confirm that the object was restored. The message lists what remains, such as `Position: now 80, 2, 3, before 4, 2, 3` or `Owned spans still registered: ...`, or identifies the uncertain state. The footer repeats this information while you inspect the object. Further edits to that object are refused with `is blocked by incomplete cleanup`. Other objects remain editable, and your earlier edits stay in place.

Click **Guide** to open the guide for the failed operation. **Guide** is unavailable when no failure is shown.

To resolve an incomplete cleanup, click **Recheck**. Recheck finds and inspects the object again, compares it with the scene patch file and applies the file where they differ. Only what the file names remains. For example, Recheck removes a detection addition left half built by the failed edit and reuses an addition named in the file instead of building it again. When the object agrees with the file, you can edit it again. If Recheck cannot make them agree, the message lists the remaining differences and further edits remain blocked. Reloading the native map editor also allows editing again. **Refresh** only reads the scene; it changes nothing and does not allow editing to resume.

The editing session never retries a failed edit automatically. Each correction or Recheck checks the object, its prerequisites and its state again before changing anything.

## Leave the native map editor

When work is outstanding, the exit confirmation names it, for example `Unsaved work: native map data, scene patch file.` **Cancel** returns to the native map editor. **Exit** discards the unsaved work.

For the fields the native map editor's Save writes, see [scene patch file v1](scene-patch-file-v1.md).

## Reload the native map editor

Click **Reload Map Editor...** in Scene patches to request a native map editor reload. The confirmation says that native Undo/Redo history ends on reload. Click **Cancel** to leave the Map Editor and its work open. **Save and Reload** saves the required native and patcher work, then reloads after all writes complete. **Discard and Reload** discards remaining unsaved work. Writes already completed during a partial Save stay on disk. If Save fails, the Map Editor stays open and reports completed and outstanding writes.

While a scene patch file version choice is pending or the file needs repair, reload and exit offer discard or cancel. Save is offered only after you choose **Use file version** or **Keep editor version** and, in repair state, after the backup succeeds. After an unrecovered native history error, the native map editor may be unable to prove that native map data is clean. The confirmation identifies outstanding work so you can cancel or discard it.

Reload relaunches the requested map definition. For the same map, the patcher records the camera's ground location in map coordinates and requests a return once the native map editor and camera are ready again. The request survives mod replacement in the same process, expires after five minutes and is discarded if another map opens. Before reporting arrival, the patcher checks the settled horizontal ground position. Selection, camera angle and native history are not preserved.

Reload does not replace an installed DLL. Installing or replacing one requires a process restart. A journey in Railroader is still needed to establish the rendered prompts, reload and discard behavior, and return to the working location for the tested build and layout.
