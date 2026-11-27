<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Duplicate a scene object

Use **Duplicate** to make a mod object from a supported scene object. Click the ground to place the mod object. The original stays as it is. For example, you can add a station building for a new station, a speed sign or milepost for a new siding, or a crossing with signals for a new road. You can also duplicate station map icons, location signs, bridges, whistle signs, and spawn points that players can teleport to.

## Before you start

- Open the native map editor and the Scene Patcher window, then choose the mod to edit.
- Inspect the object you want to duplicate to find **Duplicate** on the **Object** tab. For a crossing with signals, select its controller.

A crossing without signals has no controller or separate object to inspect. Select the crossing in the outliner. The inspector shows the crossing's name under **Crossing**, with **Duplicate** and **Remove modification**. **Duplicate** works the same way here as on the **Object** tab.

You can duplicate an entire group, such as a station building with its parts, or just one part. Other supported objects include station map icons, location signs, road and rail bridges, Generic track markers, mileposts, crossing pads, and spawn points. If you can't duplicate an object, hover over **Duplicate** to see why.

| Object | Tooltip reason |
| --- | --- |
| Starter placement marker | New game setups can't use mod objects. |
| Inspection-only object | You can only inspect this object. |
| Generic track marker, milepost, crossing controller, or crossing pad that Scene Patcher can't edit | The reason shown in **Supported edits**. |
| Any other family, such as a crossing mast | This family can't be duplicated. |
| Group or crossing that contains an object of an unsupported family | It contains objects that can't be duplicated. |

## Duplicate and place the object

1. Click **Duplicate**.

   A blank object window opens with the source object's name in the title. It shows only the **Object** tab, with **Place** and **Cancel**. The outliner and scene patch file have no entry for the mod object yet.

1. Move the camera to the new location as usual. You can still use the native map editor. Other Scene Patcher controls, including **Select in world**, are unavailable until you place the object or click **Cancel**.

1. Click **Place**. A ghost of the object follows the pointer over the ground, with the instruction **Left-click to place**. For a crossing, the ghost omits whistle signs and crossing pad surfaces. For a station map icon, it shows the map's icon. The ghost disappears when the pointer is over the Scene Patcher window or there's no ground under the pointer.

1. Left-click the ground to place the object at the ghost's position. The mod object keeps the source object's rotation.

   To stop placing without adding the object, click **Release** or press Esc. The ghost disappears and the blank object window shows **Place** and **Cancel** again.

After placement, the window shows all four tabs for the mod object. The outliner lists it under its new location. Scene Patcher uses the source object's name with the new place name and adds a number. It counts the map's own objects of the same kind at that location first. For example, Nantahala already has one station building. If you duplicate Alarka Jct Station there, it appears as `Nantahala Station (2)`. A duplicated spawn point with a teleport name shows that name. The mod object's **Origin** is **Mod**. The source scene object's **Origin** is **Map**.

To stop duplicating without adding an object, click **Cancel**. The window returns to the source object. Closing the Scene Patcher window or reloading the map also stops the operation.

If you select another object in the outliner before placing the mod object, Scene Patcher asks whether to cancel. Click **Cancel Duplicate** to inspect the selected object and discard the unplaced mod object. Click **Keep duplicating** to return to the blank object window.

## Finish the mod object

Some mod objects need another step before they work in the game. See [Mod objects](scene-patch-file-v1.md#mod-objects) for what each mod object contains.

- **Generic track marker or milepost.** The game binds the mod object to the nearest track within 25 meters of where you placed it. The **Connections** tab shows the track. If there's no track within 25 meters, the mod object appears under **Problems** in the outliner. Set the binding on the **Connections** tab. To change the marker ID, click **Set replacement ID** on the same tab. **Clear replacement ID** is unavailable because the mod object always has its own marker ID.
- **Crossing with signals.** Lay or change the track at the new location first. Trains can't trigger the new crossing until you add detection. The controller appears under **Problems** until then. On the controller's **Connections** tab, enter a unique ID under **Add detection**, click **Add detection**, and select the tracks. To change detection later, click **Edit detection addition**. See [Add crossing detection](adding-crossing-detection.md). Then bind each crossing pad on its **Connections** tab. See [Rebind crossing pads](rebinding-crossing-pads.md). To move the duplicated crossing later, move or rotate its controller. Its masts and bell move together, and its pads stay where they are. See [move or rotate the whole grade-crossing setup](moving-crossing-equipment.md#move-or-rotate-the-whole-grade-crossing-setup). Move a single mast or the bell as you would for any crossing.
- **Crossing without signals.** Bind each crossing pad on its **Connections** tab. Move each crossbuck as you would any other object. The crossing stays where you placed it. If several crossings share a name, as the three below Old River Rd do, Scene Patcher duplicates the crossing you selected.
- **Crossing pad.** Bind the pad on its **Connections** tab.
- **Rail bridge.** Lay track over the bridge in the native map editor.
- **Station map icon.** The icon isn't linked to a station. To add a station, create it in the native map editor.
- **Spawn point.** Without a teleport name, players have to type the full object name. Give the spawn point a shorter name they can use. On the **Object** tab, enter a name in **Teleport name** and click **Set**. The name can't contain a slash (`/`). Players teleport there by typing `/tp` followed by the name in the console. Put quotation marks around names with spaces, for example `/tp "Bryson Yard"`. To remove the name, clear the field and click **Set**. A new game never starts at a duplicated spawn point.

## Edit or remove a mod object

You can move, rotate, enable, or disable a mod object if its family supports that edit. Moving a duplicated spawn point keeps its teleport name. You can also duplicate a mod object. Scene Patcher uses the same original scene object for both mod objects.

To remove a mod object, inspect it and click **Remove modification**. This removes the object and edits made to its parts. For a crossing without signals, select the crossing in the outliner and click **Remove modification**.

Placing a mod object adds one step to the native map editor's history. Undo removes the mod object, and Redo places it again.

Click **Save** to write the mod object's modification. The modification names the original scene object as its `source`. See [Mod objects](scene-patch-file-v1.md#mod-objects) for the saved format and how Scene Patcher rebuilds the mod object each time the map loads.

## If something goes wrong

- **The click places nothing.** Check the message in the blank object window. If there's no ground under the pointer, move the pointer over the ground and left-click.
- **The mod object is missing after a load.** Check **Log** for the modification and why it failed. Most mod objects need the source object or an identical one in the loaded map. See [Mod objects](scene-patch-file-v1.md#mod-objects) for how Scene Patcher chooses a source, including the fallback for spawn points.
