<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Duplicate a scene object

Duplicate makes a new object from an object of the map and places it where you click. The new object is a mod object: your mod makes it, and the game's own object stays as it is. Use it to give a new station an existing station building, to add a station map icon, a location sign or a bridge, to give a new siding its own speed sign, whistle sign or milepost, or to add a spawn point that players teleport to.

## Before you start

- Open the native map editor and the Scene Patcher window, and choose the mod to edit.
- Inspect the object to duplicate. **Duplicate** is on the **Object** tab.

**Duplicate** is available for a group, such as a station building with its parts, for a single part of a group, for a station map icon, for a location sign, for a bridge, a road bridge or a rail bridge, for a Generic track marker and a milepost, and for a spawn point. For every other object the button is inactive, and its tooltip gives the reason:

| Object | Reason |
| --- | --- |
| Starter placement marker | No start setup reads a duplicated marker. |
| Inspection-only object | Inspection-only objects have no edits. |
| Generic track marker or milepost that Scene Patcher can't edit | The reason that Supported edits gives for it. |
| Any other family, such as a crossing | This family can't be duplicated. |
| Group that holds an object of such a family | It holds objects that can't be duplicated. |

## Duplicate and place the object

1. On the **Object** tab, click **Duplicate**.

   A blank object window opens. Its title names the object you duplicate, and it shows only the **Object** tab with **Place** and **Cancel**. Nothing is added yet: the outliner and the scene patch file hold nothing of the new object.

1. Move the camera to where the object goes. The camera moves as always, and the native map editor stays available. The other controls of the Scene Patcher window, **Select in world** among them, wait until you place the object or click **Cancel**.

1. Click **Place**. A ghost of the object, a copy that is shown only, stands on the ground under the pointer and follows it, with the instruction **Left-click to place**. Over the Scene Patcher window, or where no ground is, the ghost is hidden. For a station map icon, the ghost is the map's icon.

1. Left-click the ground. The object is placed where the ghost stands, with the rotation of the object you duplicated.

   To stop placing without placing anything, click **Release** or press Esc. The ghost leaves the pointer, and the blank object window shows **Place** and **Cancel** again.

After the click, the window shows the new object with all four tabs. The outliner lists it under the place where it stands.

What the new object is depends on what you duplicated:

- A part becomes an object of its own. It stands beside the group it came from, not in it, and moving that group leaves it where it is.
- A location sign gets its own ground mask at its new place, and the terrain there follows it.
- A bridge stands fully built and active, whatever the construction or progression state of the bridge you duplicated, and neither ever switches it. A rail bridge brings no track: lay ordinary track over it in the native map editor.
- A Generic track marker or milepost gets a marker ID of its own, which no other scene marker uses, so the object you duplicated keeps its registration. It binds to the track that the game finds at the placed position: the nearest track within 25 metres. Its **Connections** tab shows that track. Where no track lies within 25 metres, the new object stays unbound and the outliner lists it under **Problems** until you set its binding on the **Connections** tab. To give it another marker ID, use **Set replacement ID** on the **Connections** tab; a mod object always keeps an ID of its own, so **Clear replacement ID** is unavailable. A copy of an origin milepost is never the origin of its prefix: the milepost you duplicated stays the origin.

- A spawn point gets a **Teleport name** field on the **Object** tab, empty after placing. Type a name and click **Set**. The name becomes the new spawn point's object name in the game, so players teleport to it with `/tp` and the name in the console, and find it in the quick search, as they do for the map's own spawn points. In the console, a name with a space needs quotation marks, such as `/tp "Bryson Yard"`. The outliner and the inspector heading then show the name. While the field is empty, the tab says that players can reach the new spawn point only by typing its full object name, which is very hard. A name can't contain a slash (/). To remove the name, clear the field and click **Set**. Moving the spawn point keeps its name. The new spawn point ranks below every spawn point of the map, so a new game never starts there, and it keeps the radius of the spawn point you duplicated.

To end without a new object, click **Cancel**. The window shows the object you started from. Closing the Scene Patcher window or reloading the map also ends Duplicate.

If you select another object in the outliner before you place the object, Scene Patcher asks whether to cancel Duplicate. Click **Cancel Duplicate** to end it and inspect the object you selected, or **Keep duplicating** to stay in the blank object window.

## Edit or remove a mod object

A mod object takes the edits of its family: move, rotate, enable or disable it like any other object of that family. You can duplicate it again; the new object is made from the same object of the map.

To remove a mod object, inspect it and click **Remove modification**. The object leaves the map, together with your edits of its parts.

The placing click is one step in the native map editor's history: Undo removes the mod object and Redo places it again.

## What Save writes

Save writes one modification for the mod object. It names the object of the map as `source`, with a `build` that identifies its model and build, the mod object's own path as `target`, and the placement. Each load builds the mod object again from its source. When a game update removes that object of the map, the load builds the mod object from an identical object of the map instead, and the player log holds a warning that names both. See [Mod objects](scene-patch-file-v1.md#mod-objects) in the format reference.

A duplicated station map icon is a visual object. It shows on the map at its place, and no station is linked to it. To add a station, create it in the native map editor.

## If something goes wrong

- **The click places nothing.** The message in the blank object window gives the reason. Where no ground is under the pointer, left-click the ground.
- **The mod object is missing after a load.** The Log names the modification and the reason, such as a source that no longer matches an object of the map. A mod object needs its source, or an identical object, in the loaded map.
