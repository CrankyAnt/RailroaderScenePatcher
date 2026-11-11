<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Edit and save fixed placement in the map editor

This guide shows how to move, rotate, disable and restore a supported scene object in the map editor, and how Save, Undo and Redo treat those edits. The editor currently supports the independent board described in [author fixed scene placement](authoring-fixed-placement.md). The automated tests cover the behavior below with controlled scene, history and storage dependencies. A recorded journey in Railroader for these editor steps is not yet part of this guide.

## Before you start

Open your MapMod's Map definition from the Editor menu. The patcher edits only the active edited MapMod: it reads and writes `Data/scene-patches.json` and `Info.json` in that pack and never writes to an inherited pack. You do not need to create the document or declare the dependency first; Save does both.

Open **Scene patches**. The status line reads **Authoring ready** with the document path. If it names a problem instead, the document is protected: an unreadable document, a newer format, or a document for another map is never overwritten. Correct the file outside the editor and reload the editor.

## Set a numeric placement

1. Select **Refresh inventory and verify stored target identities**, search for the object and select its **Inspect** button, as described in [inspect scene objects](inspect-scene-objects.md). For an object with a fixed-placement adapter, the six fields show its inspected position in map coordinates and its rotation in degrees. Other observations are inspection only.
2. Change the values. Use a decimal point.
3. Select **Apply placement**.

The object moves at once and the document receives one modification for that object, holding the fixed position and the rotation as a quaternion. Applying again replaces the values; it does not add a second modification or accumulate movement. **Read inspected values** refills the fields from the inspected observation.

Every edit is bound to that observation. The patcher refuses the edit when the object, its placement, activity, bindings or dependencies no longer match what you inspected, or when the inventory is stale. A completed edit refreshes the inventory itself. After Undo, Redo or a native edit, refresh the inventory before the next edit; the refusal names this when you forget.

## Disable or enable the object

Select **Disable object** or **Enable object**. The document stores the choice as `"active": false` or `"active": true` in the same modification as the placement. A disabled board releases its culling and terrain mask through the game's own component lifecycle. A document with an activity change needs patcher version 0.2.0 or later.

## Undo and redo

Each completed edit is one entry in the editor's own history, between your native edits. Use the editor's **Undo** and **Redo**. They reverse and reapply the object's state and the document together.

Reloading the editor ends that history. The patcher does not carry Undo or Redo across a reload.

If the editor's Undo or Redo itself stops on an error, the panel shows **Incomplete operation** and the editor's history can no longer be trusted to record edits. Save still writes the current state, but the title star, the Save button and the exit confirmation keep reporting unsaved native map data until you reload the editor.

If a patcher edit cannot be reversed or reapplied, the panel also shows **Incomplete operation**. The document keeps the state the panel shows, the editor stays unsaved, and Save remains available.

## Remove a modification

Select **Remove modification** to delete this MapMod's modification of the selected object, whether it is saved or not. The object returns to its effective underlying state: the state the loaded map gives it, with the remaining modifications in your document still applied. It does not return to an earlier patched position. Removal is an edit like any other: Undo restores the modification and Save writes the result.

A modification that holds content this patcher version does not understand is preserved and cannot be edited or removed in the editor. When several modifications in a manually written document target the same object, combine them in the file first.

## Save

Use the editor's **Save**. One Save covers three writes for the active edited MapMod:

- the native map data,
- `Data/scene-patches.json`,
- the patcher dependency in `Info.json`.

Save adds `crankyant/scene-patcher >=<version>` when the dependency is missing, and raises the minimum only when the document needs a newer patcher. A constraint that already requires that version or a later one stays unchanged, and so does an upper bound. Save writes each patcher file only when its content differs. Save never reloads the editor.

The title star and the Save button follow all three writes. They stay active after Undo, Redo or a new edit whenever the current state differs from the last complete Save, including when a different history reaches the same number of changes.

## Retry after a failed Save

When a write fails, the editor shows **Save Failed** with the cause, the writes that completed and the writes still outstanding. The panel repeats that report. Completed writes are not reversed, your edits stay in memory and the editor stays unsaved.

Correct the cause, for example a locked or read-only file, and select **Save** again. The retry writes only what is outstanding. If `Data/scene-patches.json` changed outside the editor after the patcher read it, the retry refuses to overwrite it; reload the editor to read the file again.

Save refuses to write the dependency when an existing constraint in `Info.json`, such as `^0.1.0`, excludes the version the document needs. Correct that constraint and save again.

## Retry after a failed edit

A failed edit restores the object and the document and adds nothing to the history. If the patcher cannot verify that restoration, it blocks further edits of that object and says so. Select **Recheck after failed edit**: the patcher compares the object with the document, applies the document's state if they differ, and unblocks the object once they agree. Reloading the editor also clears the block.

## Leave the editor

When work is outstanding, the exit confirmation names it, for example `Unsaved work: native map data, patch document.` **Cancel** returns to the editor. **Exit** discards the unsaved work.

For the fields the editor writes, see [scene-patch document v1](scene-patch-document-v1.md).
