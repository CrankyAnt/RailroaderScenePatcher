<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Edit and save fixed placement in the native map editor

Use the native map editor to move, rotate, disable, or restore a supported scene object. This guide also explains how Save, Undo, and Redo handle those edits. You can currently edit [independent boards](moving-boards.md), [the Ela bridge](placing-bridges.md), [baked scenery groups and their parts](moving-baked-scenery-groups.md), and [player spawn points](moving-spawn-points.md).

For other operations, see the [modder manual](modder-manual.md). To make changes in a text editor, see [Write a scene patch file](writing-scene-patch-files.md).

## Before you start

Open your mod's map definition from the Editor menu. The patcher reads and writes `Data/scene-patches.json` and `Info.json` in the edited mod only. It doesn't access those files in mods that supply map layers or terrain tile sets. Save creates the scene patch file and declares the patcher dependency when needed.

A new scene patch file names the map definition's bottom map layer as its scene target map. If the definition has no map layers, the file names the definition itself. When the bottom map layer is the base-game map, the file also applies to a save of that map, where your map-changing script or map patch supplies your native changes. An existing scene patch file keeps its scene target map. See [the scene patch file reference](scene-patch-file-v1.md#scene-patch-file-fields).

Open **Scene Patcher**, then click the page icon to open the session details. Under **Editing**, **Editing ready for** followed by the scene patch file path means you can begin.

If this section reports a problem, the scene patch file is protected. The patcher never overwrites an unreadable file, a file in a newer format, or one for another map. The **Scene patch file** section identifies the problem. Correct the file and read it again, or back it up and make a fresh start. See [Recover a protected scene patch file](recover-scene-patch-files.md).

## Choose a scene patch file version

The patcher checks `Data/scene-patches.json` when the game regains focus and when you click **Refresh** or **Save**. It compares content, so changes to whitespace, object-member order, or the order of entries with stable identities do not require a version choice.

If the content changes outside the editing session, the **Version choice** panel replaces the inspector tabs. It says `The scene patch file changed outside scene patch editing. Save won't overwrite it until you choose a version.` and explains each choice.

Until you choose a version, only **Refresh** and the session details remain available outside the **Version choice** panel in Scene Patcher. Disabled controls show the reason in their tooltips. The message area keeps its height while you choose a version. Scroll it to read results that extend below its bottom edge. The native map editor's **Save** button stays enabled, but writes nothing. Clicking it shows `Save refused.` and the reason in the message area. If **Scene Patcher** is closed, a **Save refused** alert shows the reason instead.

Choose how to handle the changed file:

- **Use file version** applies the changed file in the current session and discards unsaved scene patch edits. **Scene Patcher** explains this loss before confirmation. Unsaved native map work is unaffected.
- **Keep editor version** retains the session's scene patch file and edits. Save can write again. The next Save replaces the changed file with the editor version.
- **Import** brings the file's changes into the session. See [Import outside changes](#import-outside-changes).

After **Use file version**, discarded steps remain in native Undo and Redo under their original action names and menu titles, but have no effect. Neither these steps nor any scene patch edit recorded before the choice can change the scene patch file through Undo or Redo. The scene patch status becomes **Saved** if the session's scene patch content equals the file on disk, or **Unsaved** if it differs.

When you use **Use file version**, the footer counts the applied and refused modifications. If the patcher refuses a modification, such as a construction segment detach, it names that modification and gives its reason. The refused modification stays in the scene patch file. Its **Changes** tab shows `Refused.` and the reason instead of listing changes. Modifications refused during world load appear the same way.

For a modification refused by **Use file version**, the outliner marks the object with × instead of `*`, and its state line shows `× REFUSED` instead of `* CHANGED`. The **Problems** filter doesn't list it because a problem is a defect of the object itself. The refusal concerns the scene patch file and also appears in the native log. If you remove that refused modification in the editor, native **Undo** returns it to the scene patch file without applying it.

None of these choices requires a reload. The version choice remains visible after the action that detected the change. Undo, Redo, and **Remove modification** update reload feedback to reflect the current session. If the file changed while the game was closed, the next native map editor load reads it without asking you to choose a version.

### Import outside changes

Click **Import** in the **Version choice** panel. The **Import** panel opens in its place and lists every change since the patcher last read or wrote the file. Each is marked **New**, **Changed**, or **Removed** and explains its effect, including changes outside the modifications.

The panel then lists conflicts. Each shows the editor and file versions, followed by a choice between them. A conflict occurs when:

- Your scene patch edits and the changed file both change the same entry.
- Your edits and the file change one object under different modification IDs.
- Both introduce the same modification ID with different content.

For each conflict, click **Editor version** or **File version**. **File version** discards your edits for that conflict. As with **Use file version**, their steps remain in native Undo and Redo under their original names but have no effect.

Click **Import** to apply all listed changes and conflict choices together. The button is unavailable until every conflict has a choice. Imported changes become unsaved scene patch work; nothing is written until you click **Save** in the native map editor. Your other scene patch edits keep their values. The message area shows the result.

Click **Cancel** to close the panel without changes and discard the conflict choices. You still need to choose a file version.

Import adds no native Undo or Redo step. After an import without conflicts, one Undo reverses your own last action before the import. Undo or Redo of an earlier step never overwrites a value applied by the import. If any change fails, the patcher restores the state from before the import and explains the failure in the message area.

While the **Import** panel is open, clicking **Save** writes nothing and shows `Save refused.` followed by `An import is open in Scene Patcher. Click Import or Cancel first.` After import, the scene patch status is **Saved** if the session's scene patch content equals the file on disk, or **Unsaved** if it differs. A file that needs repair cannot be imported.

### Repair the scene patch file

If the patcher cannot read the file as a valid scene patch file, or its format version is missing, unrecognized, or newer, **Scene Patcher** enters **File needs repair**. The **Repair** panel replaces the inspector tabs. It shows the diagnosis with line numbers and offers **Keep editor version**, **Details**, and **Copy**, whose tooltip reads **Copy to clipboard**. **Use file version** and **Import** are unavailable.

Only **Refresh** and the session details remain available outside the panel. Disabled controls show the reason in their tooltips. Clicking **Save** writes nothing and shows `Save refused.` with the diagnosis in the message area. See [Recover a protected scene patch file](recover-scene-patch-files.md).

When a repair check finds that the file is readable but still changed, **Scene Patcher** says `The file is readable again.` and shows the normal version choice. If its content is unchanged, the choice clears.

## Set a numeric placement

For an edit that keeps the same object selected and lets you review changes before **Apply**, see [complete an edit with a guided task](completing-guided-tasks.md).

1. Find the object in the [outliner](inspect-scene-objects.md) or [select it in the world](select-world-targets.md), then confirm its identity.
2. On the **Placement** tab, use **Numeric** mode to enter the values. Use a decimal point.
3. Click **Apply**.

For an object that supports fixed placement, six fields show the position and rotation you inspected. Position uses map coordinates, and rotation uses degrees. The **Position (m)** and **Rotation (deg)** rows each have **X**, **Y**, and **Z** fields. For objects that can't be edited, two lines of text show the values instead, such as `X 4361.091  Y 528.405  Z 5380.431 (m)`. The amber **Editing** message explains why editing is unavailable.

Fields display three decimal places and never use scientific notation. An unchanged field keeps the object's exact value. Changing X alone therefore leaves the other axes and rotation unchanged.

**Apply** is disabled while the fields match the current placement. **Supported edits** on the **Object** tab explains why it is unavailable. As soon as a keystroke changes a value, **Apply** becomes available without requiring Enter or leaving the field. It becomes disabled again if the value matches the current one. Applying the object's placement without this mod removes the placement from the modification.

Applying moves the object and creates or updates its modification in the scene patch file. The modification stores the fixed position and stores rotation as a quaternion. If an axis's stored value hasn't changed, its text in the file stays the same. For example, changing only Z doesn't replace X with a longer number read back from the game. Applying again replaces the values in the same modification. It doesn't add a second modification or accumulate movement. Click **Read** to refill the fields from the inspected observation.

### Check the result and refresh

Every edit uses the observation you inspected. The patcher refuses an edit if the object, its placement, activity, bindings, or dependencies have changed since that observation, or if the inventory is stale.

The first message slot shows the action group and **Working...** until the edit finishes, then shows a result such as `Placement completed.` A completed edit refreshes the inventory a moment later. After Undo, Redo, or a native edit, click **Refresh** before editing again. If you forget, the refusal tells you to refresh.

A refused edit, or one that does not run, reports why. The message area shows the newest message at the top and dims older messages beneath it. Every message starts with its action group. A **Gizmo** result keeps the previous **Copy ID** result below it. Only the latest automatic follow-up message, such as **Terrain output**, remains, so follow-up messages do not displace the action result. Scroll if the messages do not fit. Inspecting another object clears them all.

The line above the messages shows why the inspected object cannot currently be edited under **Editing**, or identifies a pending world choice, gizmo, or prepared snap. An unavailable action stays visible but disabled. Its tooltip explains why, and the log records the reason. Reasons include a stale inventory and what made it stale, unreadable values, or an edit requested while a world selection or gizmo is pending.

## Disable or enable the object

On the **Object** tab, click **Disable** or **Enable**. The scene patch file stores `"active": false` or `"active": true` in the same modification as the placement. Disabling a board makes the game release its culling and terrain mask. A disabled bridge stops colliding.

Generic activity requires patcher 0.2.0. Native board, bridge, and track-marker activity capabilities require 0.5.0. Save derives the dependency minimum from the scene patch file's content. See [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

After a board edit, the **Terrain** section on the **Changes** tab shows the state of the terrain output the game regenerates at the old and new positions. Save does not write this generated output. See [move a board with its terrain effects](moving-boards.md) for completion, retry, and limits.

## Undo and redo

Each completed edit adds one entry to the native map editor's history alongside your other map edits. Use the native map editor's **Undo** and **Redo** to reverse and reapply the object's state and the scene patch file together. Reloading ends the history. The patcher doesn't preserve Undo or Redo across a reload.

If the native map editor's Undo or Redo stops on an error, the session details show **Incomplete operation**. The history can no longer be trusted to record edits. Save still writes the current state. Until you reload the native map editor, however, the title star, Save button tint and tooltip, and exit confirmation continue to report unsaved native map data.

If a patcher edit cannot be reversed or reapplied, the session details also show **Incomplete operation**. The scene patch file keeps the state shown in the panel. The native map editor stays unsaved and Save remains available.

## Remove a modification

Click **Remove modification** to remove this mod's saved or unsaved modification of the selected object. The object returns to its effective underlying state, the state the loaded map gives it with the remaining modifications in your scene patch file still applied. It doesn't return to an earlier position from a scene patch. Undo restores the modification. Save writes the removal.

If another modification in the file names this one in `dependsOn`, removal is refused. The message names the modifications you must change or remove first. This also applies to **Clear activity** and any other edit that would leave the modification empty.

The native map editor preserves modifications with content this patcher version does not understand. You cannot edit or remove them there.

A modification whose dependency is missing, fails, or forms a cycle does not take effect and cannot be edited. The refusal explains why. You can remove it if no other modification names it in `dependsOn`. In a cycle of two or more modifications, each is named by another, so none can be removed in the editor. Break the cycle in the file and reopen the native map editor. If a manually written file has several modifications for one object, combine them in the file first.

## Save

Click **Save** in the native map editor. A single Save writes three things for the edited mod:

- The native map data.
- `Data/scene-patches.json`.
- The patcher dependency in `Info.json`.

Save adds or raises `crankyant/scene-patcher >=<minimum>` to the highest minimum required by the scene patch file's content. Legacy fixed placement needs `0.1.0`, generic activity needs `0.2.0`, and newer capabilities keep their `0.5.0` requirements. See the [dependency reference](scene-patch-file-v1.md#dependency-minimum). A constraint that already requires a sufficient version stays unchanged, as does an upper bound. Save writes each patcher file only if its content differs. It never reloads the native map editor.

The title star, the Save button's orange tint, and its **Unsaved Changes** tooltip reflect all three writes. They appear whenever an edit, Undo, or Redo leaves a state different from the last complete Save. This includes changes only to the scene patch file and a different history with the same number of changes. The Save button is always enabled. When nothing is unsaved, Save writes no patcher file.

## Retry after a failed Save

A refused Save writes nothing, for example while you need to choose a file version. **Scene Patcher** shows the reason in its message area. If the window is closed, a **Save refused** alert shows it instead.

If a write fails, the native map editor shows **Save Failed**, the cause, and the completed and outstanding writes. The session details repeat this under **Last Save failed**. Completed writes stay on disk. Your edits remain in memory and the native map editor stays unsaved.

A failed write leaves the file it was replacing intact. The patcher first writes the complete new content for `Data/scene-patches.json` or `Info.json` to a neighboring file ending in `.saving`, then replaces the original. If writing stops midway, the previous file stays unchanged. On a first Save, no original file exists yet. The patcher removes the unfinished `.saving` file. If an interruption such as a crash leaves one behind, the next Save replaces it.

If another program, such as a virus scanner or sync client, briefly holds the file open, Save retries for up to half a second before reporting failure.

Correct the cause, such as a locked or read-only file, then click **Save** again. The retry writes only what remains outstanding. If `Data/scene-patches.json` changed outside the session after the patcher read it, the retry refuses to overwrite it. Click **Use file version**, **Keep editor version**, or **Import** before retrying. A file that needs repair requires a backup first.

Save refuses to write the dependency if an existing constraint in `Info.json`, such as `^0.1.0`, excludes the version the scene patch file needs. Correct the constraint and save again.

## Retry after a failed edit

The message area distinguishes three outcomes and explains how to correct each problem:

- `refused` means the patcher stopped before changing anything. The scene, scene patch file, and history are unchanged. Correct the value or selection named in the message and apply again. If the object changed or the inventory is stale, click **Refresh**, inspect the object again, and repeat the action.
- `failed and was undone` means the change started but failed. The patcher restored the object and verified the restoration. The scene patch file is unchanged and no history step was added. Correct the cause and apply again. For a board, the message is `was undone; waiting for terrain`. The object is restored, but the game is still regenerating terrain at its restored placement. An edit before that finishes receives the same message. Once the terrain is updated, edit again or click **Retry** on the **Changes** tab. Recheck is not needed.
- `failed; cleanup is incomplete` means the patcher could not verify the object's restoration. The message lists what remains, for example `Position: now 80, 2, 3, before 4, 2, 3` or `Owned spans still registered: ...`, or identifies the uncertain state. The footer repeats this information while you inspect the object. Further edits to it are refused with `is blocked by incomplete cleanup`. Other objects remain editable and your earlier edits stay in place.

Click **Guide** to open the guide for the failed operation. The button is unavailable when no failure is shown.

### Resolve an incomplete cleanup

Click **Recheck** to find and inspect the object again, compare it with the scene patch file, and apply the file's state where they differ. Only state named in the file remains. For example, Recheck removes a detection addition left half built by the failed edit. It reuses an addition already named in the file instead of rebuilding it. Before changing anything, Recheck checks the object's modifications as a world load does. If one is refused, for example because the source of a crossing addition does not conform to its pattern, Recheck is `refused` with that reason and changes nothing. A refused Recheck does not end an incomplete cleanup: edits of the object stay blocked. Reload the native map editor, then correct or remove the refused modification.

Once the object agrees with the file, you can edit it again. If Recheck cannot make them agree, the message lists the remaining differences and edits stay blocked. Reloading the native map editor also allows editing again. **Refresh** only reads the scene. It changes nothing and does not lift this block.

The editing session never retries a failed edit automatically. Every correction or Recheck checks the object, its prerequisites, and its state again before making changes.

## Leave the native map editor

If work is outstanding, the exit confirmation names it. For example, `Exit discards unsaved changes to the map data and scene patch file.` Click **Cancel** to return to the native map editor or **Exit** to discard unsaved work.

For the fields written by Save, see [scene patch file v1](scene-patch-file-v1.md).

## Reload the native map editor

Click **Reload Map Editor...** in **Scene Patcher**. The confirmation warns that reloading ends native Undo/Redo history. Click one of these buttons:

- **Cancel** keeps the native map editor and your work open.
- **Save and Reload** saves the required native and patcher work, then reloads once every write completes.
- **Discard and Reload** discards remaining unsaved work. Writes completed during a partial Save stay on disk.
- **Reload** replaces **Save and Reload** and **Discard and Reload** when all work is saved, because the reload discards nothing.

If Save fails, the native map editor stays open and reports the completed and outstanding writes.

While a file version choice is pending or the file needs repair, reload and exit offer only discard or cancel. Save becomes available after you click **Use file version** or **Keep editor version**. In repair state, the backup must also succeed. After an unrecovered native history error, the native map editor might be unable to prove that native map data is clean. The confirmation identifies the outstanding work so you can cancel or discard it.

Reload reopens the requested map definition. When reloading the same map, the patcher records the camera's ground location in map coordinates. It requests a return to that location once the native map editor and camera are ready. The request survives mod replacement in the same process, expires after five minutes, and is discarded if another map opens. Before reporting arrival, the patcher checks the settled horizontal ground position. Reload doesn't preserve selection, camera angle, or native history.

Reload does not replace an installed DLL. Installing or replacing one requires restarting the process. A journey in Railroader is still needed to verify the rendered prompts, reload and discard behavior, and return to the working location for the tested build and layout.
