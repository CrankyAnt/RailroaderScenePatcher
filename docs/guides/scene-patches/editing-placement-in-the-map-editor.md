<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Edit and save fixed placement in the native map editor

This guide shows how to move, rotate, disable and restore a supported scene object in the native map editor, and how Save, Undo and Redo treat those edits. Scene patch editing currently supports independent boards, described in [move a board with its terrain effects](moving-boards.md), the Ela bridge, described in [move or disable a bridge with its collision](placing-bridges.md), and baked scenery groups and their parts, described in [move or disable a baked scenery group](moving-baked-scenery-groups.md). The automated tests cover the behavior below with controlled scene, history and storage dependencies. A recorded journey in Railroader for these scene patch editing steps is not yet part of this guide.

## Before you start

Open your mod's map definition from the Editor menu. The patcher edits only the edited mod: it reads and writes `Data/scene-patches.json` and `Info.json` in that mod and never writes to a mod that supplies a map layer or terrain tile set. You do not need to create the scene patch file or declare the dependency first; Save does both. A new scene patch file names the bottom map layer of your map definition as its target map, or your map definition itself when it has no map layers, so it also applies on a save of the base-game map that your map-changing script changes. An existing scene patch file keeps the target map it names; see [the scene patch file reference](scene-patch-file-v1.md#scene-patch-file-fields).

Open **Scene patches** and its session details (the page icon in the title bar). The **Editing** section reads **Editing ready for** followed by the scene patch file path. If it names a problem instead, the scene patch file is protected: an unreadable scene patch file, a newer format, or a scene patch file for another map is never overwritten. The **Scene patch file** section below it shows where the file is wrong and offers to read it again after you correct it, or to start fresh behind a backup; see [Recover a protected scene patch file](recover-scene-patch-files.md).

## Set a numeric placement

1. Find the object in the [outliner](inspect-scene-objects.md) or [select it in the world](select-world-targets.md) and confirm its identity. For an object with a fixed-placement adapter, the six fields of **Numeric** mode on the **Placement** tab show its inspected position in map coordinates and its rotation in degrees: a **Position (m)** row and a **Rotation (deg)** row, each with **X**, **Y** and **Z** in front of its box, as the native map editor shows a position. Other objects cannot be edited: they show the same values as two running lines of text, such as `X 4361.091  Y 528.405  Z 5380.431 (m)`, and the amber **Editing** message gives the object's own reason. Every number reads to three decimals, never in scientific notation; a field you do not change stands for the object's exact value, so changing X alone changes neither the other axes nor the rotation. **Apply** is inactive while the fields hold the object's current placement and follows your typing: it becomes available at the keystroke that makes a value differ, without Enter or leaving the field, and inactive again when the value equals the current one, and a placement equal to the object's placement without this mod removes the placement from the modification; **Supported edits** on the **Object** tab says why.
2. Change the values. Use a decimal point.
3. Select **Apply** at the bottom of the tab.

The object moves when the edit runs and the scene patch file receives one modification for that object, holding the fixed position and the rotation as a quaternion. An axis whose value the game stores unchanged keeps its text in the scene patch file, so changing Z alone does not rewrite X as a longer number read back from the game. Applying again replaces the values; it does not add a second modification or accumulate movement. **Read** refills the fields from the inspected observation.

Every edit is bound to that observation. The patcher refuses the edit when the object, its placement, activity, bindings or dependencies no longer match what you inspected, or when the inventory is stale. The first message slot shows the action group with **Working...** until the edit has run; the result then follows, such as `Placement completed.`, and a completed edit refreshes the inventory itself a moment later. After Undo, Redo or a native edit, select **Refresh** in the command band before the next edit; the refusal names this when you forget.

An edit that is refused or does not run always says why. The footer's message area lists the newest message on top and older ones below it, dimmed; each starts with the action group it came from. Of the messages that follow an edit by themselves, such as **Terrain output**, only the latest is kept, so they do not push your action's result out of the area. When the messages need more room than the area has, scroll the area. Inspecting another object clears all messages. Above the messages, the area's first line shows why the inspected object cannot be edited now, under **Editing**, or the world choice, gizmo or prepared snap that is pending. An action that cannot run stays visible and disabled, and its tooltip gives the reason. The reason also goes to the log. This includes a stale inventory, with what made it stale, entered values that cannot be read, and an edit requested while a world selection or a gizmo is pending.

## Disable or enable the object

Select **Disable** or **Enable** at the bottom of the tab of the **Object** tab. The scene patch file stores the choice as `"active": false` or `"active": true` in the same modification as the placement. A disabled board releases its culling and terrain mask through the game's own component lifecycle. A disabled bridge stops colliding. Generic activity requires patcher 0.2.0; native board, bridge and track-marker activity capabilities require 0.5.0. Save derives the dependency minimum from the scene patch file's content; see [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

After a board edit, the **Terrain** section of the **Changes** tab shows the state of the terrain output the game regenerates at the old and new positions. Save does not write that generated output; see [move a board with its terrain effects](moving-boards.md) for its completion, retry and limits.

## Undo and redo

Each completed edit is one entry in the native map editor's own history, between your native edits. Use the native map editor's **Undo** and **Redo**. They reverse and reapply the object's state and the scene patch file together.

Reloading the native map editor ends that history. The patcher does not carry Undo or Redo across a reload.

If the native map editor's Undo or Redo itself stops on an error, the session details (the page icon in the title bar) show **Incomplete operation** and the native map editor's history can no longer be trusted to record edits. Save still writes the current state, but the title star, the Save button and the exit confirmation keep reporting unsaved native map data until you reload the native map editor.

If a patcher edit cannot be reversed or reapplied, the session details also show **Incomplete operation**. The scene patch file keeps the state the panel shows, the native map editor stays unsaved, and Save remains available.

## Remove a modification

Select **Remove modification** in the footer to delete this mod's modification of the selected object, whether it is saved or not. The object returns to its effective underlying state: the state the loaded map gives it, with the remaining modifications in your scene patch file still applied. It does not return to an earlier patched position. Removal is an edit like any other: Undo restores the modification and Save writes the result. When another modification in your scene patch file names this one in `dependsOn`, removal is refused and names those modifications; change or remove them first. The same holds for **Clear activity** and any other edit that would leave the modification empty.

A modification that holds content this patcher version does not understand is preserved and cannot be edited or removed in the native map editor. A modification that does not take effect because a dependency is missing, fails or forms a cycle cannot be edited either; the refusal names the reason. You can still remove it when no other modification names it in `dependsOn`. In a cycle of two or more modifications every member is named by another, so none can be removed here: break the cycle in the file and reopen the native map editor. When several modifications in a manually written scene patch file target the same object, combine them in the file first.

## Save

Use the native map editor's **Save**. One Save covers three writes for the edited mod:

- the native map data,
- `Data/scene-patches.json`,
- the patcher dependency in `Info.json`.

Save adds or raises `crankyant/scene-patcher >=<minimum>` to the highest minimum required by the scene patch file's content. Legacy fixed placement needs `0.1.0`, generic activity needs `0.2.0`, and newer capabilities retain their `0.5.0` requirements; see the [dependency reference](scene-patch-file-v1.md#dependency-minimum). A constraint that already requires the necessary version or a later one stays unchanged, and so does an upper bound. Save writes each patcher file only when its content differs. Save never reloads the native map editor.

The title star and the Save button follow all three writes. They stay active after Undo, Redo or a new edit whenever the current state differs from the last complete Save, including when a different history reaches the same number of changes.

## Retry after a failed Save

When a write fails, the native map editor shows **Save Failed** with the cause, the writes that completed and the writes still outstanding. The session details repeat that report as **Last Save failed**. Completed writes are not reversed, your edits stay in memory and the native map editor stays unsaved. A failed write never damages the file it was replacing: the patcher writes the new content of `Data/scene-patches.json` or `Info.json` completely to a file beside it, ending in `.saving`, and only then puts it in the file's place. A write that stops midway leaves the previous file as it was, or no file at the first Save, and the patcher removes the unfinished `.saving` file. A `.saving` file left by an interrupted Save, for example after a crash, is replaced by the next Save. When another program, such as a virus scanner or a sync client, holds the file open for a moment, Save tries again for up to half a second before it reports the failure.

Correct the cause, for example a locked or read-only file, and select **Save** again. The retry writes only what is outstanding. If `Data/scene-patches.json` changed outside scene patch editing after the patcher read it, the retry refuses to overwrite it; reload the native map editor to read the file again.

Save refuses to write the dependency when an existing constraint in `Info.json`, such as `^0.1.0`, excludes the version the scene patch file needs. Correct that constraint and save again.

## Retry after a failed edit

A failed edit restores the object and the scene patch file and adds nothing to the history. If the patcher cannot verify that restoration, it blocks further edits of that object and says so. Select **Recheck** in the footer: the patcher compares the object with the scene patch file, applies the scene patch file's state if they differ, and unblocks the object once they agree. Reloading the native map editor also clears the block.

## Leave the native map editor

When work is outstanding, the exit confirmation names it, for example `Unsaved work: native map data, scene patch file.` **Cancel** returns to the native map editor. **Exit** discards the unsaved work.

For the fields the native map editor's Save writes, see [scene patch file v1](scene-patch-file-v1.md).
