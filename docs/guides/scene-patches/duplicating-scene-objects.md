<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Duplicate a scene object

Duplicate makes a new object from an object of the map and places it where you click. The new object is a mod object: your mod makes it, and the game's own object stays as it is. Use it to give a new station an existing station building, or to add a station map icon.

## Before you start

- Open the native map editor and the Scene Patcher window, and choose the mod to edit.
- Inspect the object to duplicate. **Duplicate** is on the **Object** tab.

**Duplicate** is available for a group, such as a station building with its parts, and for a station map icon. For every other object the button is inactive, and its tooltip gives the reason:

| Object | Reason |
| --- | --- |
| Starter placement marker | No start setup reads a duplicated marker. |
| Part of a group | A part is duplicated with its group. Duplicate the group. |
| Bridge | Bridges can't be duplicated. |
| Inspection-only object | Inspection-only objects have no edits. |
| Any other family, such as a track marker or a crossing | This family can't be duplicated. |
| Group that holds an object of such a family | It holds objects that can't be duplicated. |

## Duplicate and place the object

1. On the **Object** tab, click **Duplicate**.

   A blank object window opens. Its title names the object you duplicate, and it shows only the **Object** tab with **Place** and **Cancel**. Nothing is added yet: the outliner and the scene patch file hold nothing of the new object.

1. Move the camera to where the object goes. The camera moves as always, and the native map editor stays available. The other controls of the Scene Patcher window, **Select in world** among them, wait until you place the object or click **Cancel**.

1. Click **Place**. A ghost of the object, a copy that is shown only, stands on the ground under the pointer and follows it, with the instruction **Left-click to place**. Over the Scene Patcher window, or where no ground is, the ghost is hidden.

1. Left-click the ground. The object is placed where the ghost stands, with the rotation of the object you duplicated.

   To stop placing without placing anything, click **Release** or press Esc. The ghost leaves the pointer, and the blank object window shows **Place** and **Cancel** again.

After the click, the window shows the new object with all four tabs. The outliner lists it under the place where it stands.

To end without a new object, click **Cancel**. The window shows the object you started from. Closing the Scene Patcher window or reloading the map also ends Duplicate.

If you select another object in the outliner before you place the object, Scene Patcher asks whether to cancel Duplicate. Click **Cancel Duplicate** to end it and inspect the object you selected, or **Keep duplicating** to stay in the blank object window.

## Edit or remove a mod object

A mod object takes the edits of its family: move, rotate, enable or disable it like any other group or station map icon. You can duplicate it again; the new object is made from the same object of the map.

To remove a mod object, inspect it and click **Remove modification**. The object leaves the map, together with your edits of its parts.

The placing click is one step in the native map editor's history: Undo removes the mod object and Redo places it again.

## What Save writes

Save writes one modification for the mod object. It names the object of the map as `source`, the mod object's own path as `target`, and the placement. Each load builds the mod object again from its source. See [Mod objects](scene-patch-file-v1.md#mod-objects) in the format reference.

A duplicated station map icon is a visual object. It shows on the map at its place, and no station is linked to it. To add a station, create it in the native map editor.

## If something goes wrong

- **The click places nothing.** The message in the blank object window gives the reason. Where no ground is under the pointer, left-click the ground.
- **The mod object is missing after a load.** The Log names the modification and the reason, such as a source that no longer matches an object of the map. A mod object needs its source in the loaded map.
