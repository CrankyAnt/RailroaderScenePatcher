<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Edit and save fixed placement in the map editor

This guide shows how to move, rotate, disable and restore a supported scene object in the map editor, and how Save, Undo and Redo treat those edits. The editor currently supports independent boards, described in [move a board with its terrain effects](moving-boards.md), and the Ela bridge, described in [move or disable a bridge with its collision](placing-bridges.md). The automated tests cover the behavior below with controlled scene, history and storage dependencies. A recorded journey in Railroader for these editor steps is not yet part of this guide.

## Before you start

Open your MapMod's Map definition from the Editor menu. The patcher edits only the active edited MapMod: it reads and writes `Data/scene-patches.json` and `Info.json` in that pack and never writes to an inherited pack. You do not need to create the document or declare the dependency first; Save does both.

Open **Scene patches** and its session details (**Details** at the right end of the command band). The **Save** section reads **Editing ready for** followed by the document path. If it names a problem instead, the document is protected: an unreadable document, a newer format, or a document for another map is never overwritten. Correct the file outside the editor and reload the editor.

## Set a numeric placement

1. Find the object in the [outliner](inspect-scene-objects.md) or [select it in the world](select-world-targets.md) and confirm its identity. For an object with a fixed-placement adapter, the six fields of **Numeric** mode on the **Placement** tab show its inspected position in map coordinates and its rotation in degrees. Other objects are inspection only, and their fields are disabled.
2. Change the values. Use a decimal point.
3. Select **Apply** in the footer.

The object moves when the edit runs and the document receives one modification for that object, holding the fixed position and the rotation as a quaternion. Applying again replaces the values; it does not add a second modification or accumulate movement. **Read** refills the fields from the inspected observation.

Every edit is bound to that observation. The patcher refuses the edit when the object, its placement, activity, bindings or dependencies no longer match what you inspected, or when the inventory is stale. The first message slot shows the action group with **Working...** until the edit has run; the result then follows, such as `Placement completed.`, and a completed edit refreshes the inventory itself a moment later. After Undo, Redo or a native edit, select **Refresh** in the command band before the next edit; the refusal names this when you forget.

An edit that is refused or does not run always says why. The footer's message area lists the newest message on top and older ones below it, dimmed; each starts with the action group it came from. Of the messages that follow an edit by themselves, such as **Terrain output**, only the latest is kept, so they do not push your action's result out of the area. When the messages need more room than the area has, scroll the area. Inspecting another object clears all messages. Above the messages, the area's first line shows why the inspected object cannot be edited now, under **Editing**, or the world choice, gizmo or prepared snap that is pending. The session details list the last three messages in full under **Messages**. An action that cannot run stays visible and disabled, and its tooltip gives the reason. The reason also goes to the log. This includes a stale inventory, with what made it stale, entered values that cannot be read, and an edit requested while a world selection or a gizmo is pending.

## Disable or enable the object

Select **Disable** or **Enable** in the footer of the **Object** tab. The document stores the choice as `"active": false` or `"active": true` in the same modification as the placement. A disabled board releases its culling and terrain mask through the game's own component lifecycle. A disabled bridge stops colliding. Generic activity requires patcher 0.2.0; native board, bridge and track-marker activity capabilities require 0.5.0. Save derives the dependency minimum from the document's content; see [the document reference](scene-patch-document-v1.md#dependency-minimum).

After a board edit, the **Terrain output** section of the **Changes** tab shows the state of the terrain output the game regenerates at the old and new positions. Save does not write that generated output; see [move a board with its terrain effects](moving-boards.md) for its completion, retry and limits.

## Undo and redo

Each completed edit is one entry in the editor's own history, between your native edits. Use the editor's **Undo** and **Redo**. They reverse and reapply the object's state and the document together.

Reloading the editor ends that history. The patcher does not carry Undo or Redo across a reload.

If the editor's Undo or Redo itself stops on an error, the session details (**Details** in the command band) show **Incomplete operation** and the editor's history can no longer be trusted to record edits. Save still writes the current state, but the title star, the Save button and the exit confirmation keep reporting unsaved native map data until you reload the editor.

If a patcher edit cannot be reversed or reapplied, the session details also show **Incomplete operation**. The document keeps the state the panel shows, the editor stays unsaved, and Save remains available.

## Remove a modification

Select **Remove** in the footer of the **Changes** tab to delete this MapMod's modification of the selected object, whether it is saved or not. The object returns to its effective underlying state: the state the loaded map gives it, with the remaining modifications in your document still applied. It does not return to an earlier patched position. Removal is an edit like any other: Undo restores the modification and Save writes the result.

A modification that holds content this patcher version does not understand is preserved and cannot be edited or removed in the editor. When several modifications in a manually written document target the same object, combine them in the file first.

## Save

Use the editor's **Save**. One Save covers three writes for the active edited MapMod:

- the native map data,
- `Data/scene-patches.json`,
- the patcher dependency in `Info.json`.

Save adds or raises `crankyant/scene-patcher >=<minimum>` to the highest minimum required by the document's content. Legacy fixed placement needs `0.1.0`, generic activity needs `0.2.0`, and newer capabilities retain their `0.5.0` requirements; see the [dependency reference](scene-patch-document-v1.md#dependency-minimum). A constraint that already requires the necessary version or a later one stays unchanged, and so does an upper bound. Save writes each patcher file only when its content differs. Save never reloads the editor.

The title star and the Save button follow all three writes. They stay active after Undo, Redo or a new edit whenever the current state differs from the last complete Save, including when a different history reaches the same number of changes.

## Retry after a failed Save

When a write fails, the editor shows **Save Failed** with the cause, the writes that completed and the writes still outstanding. The session details repeat that report as **Last Save failed**. Completed writes are not reversed, your edits stay in memory and the editor stays unsaved.

Correct the cause, for example a locked or read-only file, and select **Save** again. The retry writes only what is outstanding. If `Data/scene-patches.json` changed outside the editor after the patcher read it, the retry refuses to overwrite it; reload the editor to read the file again.

Save refuses to write the dependency when an existing constraint in `Info.json`, such as `^0.1.0`, excludes the version the document needs. Correct that constraint and save again.

## Retry after a failed edit

A failed edit restores the object and the document and adds nothing to the history. If the patcher cannot verify that restoration, it blocks further edits of that object and says so. Select **Recheck** in the footer of the **Changes** tab: the patcher compares the object with the document, applies the document's state if they differ, and unblocks the object once they agree. Reloading the editor also clears the block.

## Leave the editor

When work is outstanding, the exit confirmation names it, for example `Unsaved work: native map data, patch document.` **Cancel** returns to the editor. **Exit** discards the unsaved work.

For the fields the editor writes, see [scene-patch document v1](scene-patch-document-v1.md).
