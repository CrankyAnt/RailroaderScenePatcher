<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Duplicate a scene object

Use **Duplicate** to copy a supported scene object of the map and place it where you click. Your mod creates the new object as a mod object. The original stays as it is. For example, you can add a station building for a new station, a speed sign or milepost for a new siding, or a crossing with signals for a new road. You can also duplicate station map icons, location signs, bridges, whistle signs, and spawn points that players can teleport to.

## Before you start

- Open the native map editor and the Scene Patcher window, then choose the mod to edit.
- Inspect the object you want to duplicate. **Duplicate** is on the **Object** tab.

You can duplicate an entire group, such as a station building with its parts, or one part. You can also duplicate a station map icon, a location sign, a road or rail bridge, a Generic track marker, a milepost, a crossing pad, or a spawn point. For a crossing with signals, select its controller. For a crossing without signals, select the crossing in the outliner. For other objects, the button is inactive and its tooltip gives the reason:

| Object | Tooltip reason |
| --- | --- |
| Starter placement marker | No start setup uses a mod object. |
| Inspection-only object | You can only inspect this object. |
| Generic track marker, milepost, crossing controller, or crossing pad that Scene Patcher can't edit | The reason shown in **Supported edits**. |
| Any other family, such as a crossing mast | This family can't be duplicated. |
| Group or crossing that contains an object of an unsupported family | It contains objects that can't be duplicated. |

A crossing without signals has no controller or separate object to inspect. Select the crossing in the outliner. The inspector shows **Crossing** with the crossing's name, plus **Duplicate** and **Remove modification**. **Duplicate** works the same way here as on the **Object** tab.

## Duplicate and place the object

1. On the **Object** tab, click **Duplicate**.

   A blank object window opens. Its title names the source object. Only the **Object** tab appears, with **Place** and **Cancel**. Nothing has been added to the outliner or scene patch file yet.

1. Move the camera to the new location as usual. You can still use the native map editor. Other Scene Patcher controls, including **Select in world**, are unavailable until you place the object or click **Cancel**.

1. Click **Place**. A ghost follows the pointer over the ground, with the instruction **Left-click to place**. The ghost shows the object that will be placed. For a crossing, it does not show whistle signs or crossing pad surfaces. It disappears when the pointer is over the Scene Patcher window or where there is no ground. A station map icon appears as the map's icon.

1. Left-click the ground to place the object where the ghost stands. It keeps the source object's rotation.

   To stop placing without adding the object, click **Release** or press Esc. The ghost disappears and the blank object window shows **Place** and **Cancel** again.

After placement, the window shows the new object with its four tabs. The outliner lists it under the place where it stands. Its name is the source's name with the name of that place, followed by a number. The map's own objects of the same kind at that place are counted first. For example, Nantahala has one station building, so Alarka Jct Station duplicated at Nantahala appears as `Nantahala Station (2)`. A duplicated spawn point with a teleport name shows that name. Its **Origin** is **Mod**. The scene object it came from has **Origin** **Map**.

To end Duplicate without adding an object, click **Cancel**. The window returns to the source object. Closing the Scene Patcher window or reloading the map also ends Duplicate.

If you select another object in the outliner before placing the mod object, Scene Patcher asks whether to cancel Duplicate. Click **Cancel Duplicate** to end Duplicate and inspect the selected object, or **Keep duplicating** to return to the blank object window.

## Finish the mod object

Some mod objects need another step before they work in the game. What each mod object holds is described in [Mod objects](scene-patch-file-v1.md#mod-objects) in the format reference.

- **Generic track marker or milepost.** The game binds the mod object to the nearest track within 25 meters of where you placed it. The **Connections** tab shows the track. If no track is that close, the mod object appears under **Problems** in the outliner. Set its binding on the **Connections** tab. To change its marker ID, use **Set replacement ID** on the **Connections** tab. **Clear replacement ID** is unavailable because a mod object always has its own ID.
- **Crossing with signals.** Lay or change the track at the new location first. The new crossing has no detection, so trains don't trigger it, and its controller appears under **Problems**. On the controller's **Connections** tab, enter a unique ID under **Add detection**, click **Add detection**, and select the tracks. To change detection later, use **Edit detection addition**. See [Add crossing detection](adding-crossing-detection.md). Then bind each crossing pad on its **Connections** tab. See [Rebind crossing pads](rebinding-crossing-pads.md). The controller stays at the placement point. To move the crossing, remove it and duplicate it again. Move its masts and bell as you would for any crossing.
- **Crossing without signals.** Bind each crossing pad on its **Connections** tab. Move each crossbuck as you would any other object. The crossing stays where you placed it. If several crossings share a name, as the three below Old River Rd do, Scene Patcher duplicates the crossing you selected.
- **Crossing pad.** Bind the pad on its **Connections** tab.
- **Rail bridge.** Lay track over the bridge in the native map editor.
- **Station map icon.** The icon isn't linked to a station. To add a station, create it in the native map editor.
- **Spawn point.** Without a teleport name, players have to type the full object name to get there, which is hard to enter. On the **Object** tab, enter a name in **Teleport name** and click **Set**. The name can't contain a slash (`/`). Players teleport there with the `/tp` console command and this name. Put quotation marks around names with spaces, for example `/tp "Bryson Yard"`. To remove the name, clear the field and click **Set**. A new game never starts at a duplicated spawn point.

## Edit or remove a mod object

Edit a mod object with the supported edits for its family. You can move, rotate, enable, or disable it where those edits are supported. Moving a duplicated spawn point keeps its teleport name. You can duplicate a mod object too; the new object is made from the same scene object of the map as its source.

To remove a mod object, inspect it and click **Remove modification**. This removes the object and edits made to its parts. For a crossing without signals, select the crossing in the outliner and click **Remove modification**.

The placement click is one step in the native map editor's history. Undo removes the mod object, and Redo places it again.

Save writes a modification for the mod object, with the scene object it came from as its `source`. For what Save writes and how the patcher builds the mod object at each load, see [Mod objects](scene-patch-file-v1.md#mod-objects).

## If something goes wrong

- **The click places nothing.** Read the message in the blank object window for the reason. If there is no ground under the pointer, left-click the ground.
- **The mod object is missing after a load.** Check the Log for the modification and the reason, such as a source that no longer matches an object in the map. A mod object needs its source or an identical object in the loaded map.
