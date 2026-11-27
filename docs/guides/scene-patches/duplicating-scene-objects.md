<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Duplicate a scene object

Use **Duplicate** to copy a supported map object and place the copy where you click. Your mod creates the copy as a mod object. The original stays as it is. For example, you can add a station building for a new station, a speed sign or milepost for a new siding, or a crossing with signals for a new road. You can also copy station map icons, location signs, bridges, whistle signs, and spawn points that players can teleport to.

## Before you start

- Open the native map editor and the Scene Patcher window, then choose the mod to edit.
- Inspect the object you want to copy. **Duplicate** is on the **Object** tab.

You can duplicate an entire group, such as a station building with its parts, or one part. You can also duplicate a station map icon, a location sign, a road or rail bridge, a Generic track marker, a milepost, a crossing pad, or a spawn point. For a crossing with signals, select its controller. For a crossing without signals, select the crossing in the outliner. For other objects, the button is inactive and its tooltip gives the reason:

| Object | Tooltip reason |
| --- | --- |
| Starter placement marker | No start setup uses the copy. |
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

After placement, the window shows the new object with its four tabs, and the outliner lists it under the place where it stands. Its **Origin** is **Mod**. The map object it came from has **Origin** **Map**.

The result depends on the source:

- A part becomes a separate object beside its group. Moving the group does not move the new object.
- A location sign gets a ground mask at its new location, and the terrain there follows the sign.
- A bridge starts fully built and active. Construction or progression does not switch it. A rail bridge does not include track. Lay track over it in the native map editor.
- A Generic track marker or milepost gets a marker ID that no other scene marker uses, so the source keeps its registration. The game binds the new object to the track it finds nearest the placed position, within 25 metres. The **Connections** tab shows the track. If there's no track within 25 metres, the object stays unbound and appears under **Problems** in the outliner until you set its binding on the **Connections** tab. To change its ID, use **Set replacement ID** on the **Connections** tab. A mod object always has its own ID, so **Clear replacement ID** is unavailable. If you duplicate an origin milepost, only the source remains the origin for that prefix.
- A crossing with signals becomes a separate crossing with its own controller, masts, lights, gates, bell, and crossing pads. A train at one crossing doesn't trigger the other. Editing one leaves the other alone. The whistle signs, detection, and lights, gates, and bells added by a mod stay with the original crossing.
  - The new crossing has no detection, so trains do not trigger it. Its controller appears under **Problems** in the outliner. On the controller's **Connections** tab, enter a unique ID under **Add detection**, click **Add detection**, and select the tracks. To change detection later, use **Edit detection addition**. See [Add crossing detection](adding-crossing-detection.md). Lay or change the track at the new location first.
  - Each crossing pad gets its own marker ID, but no track binding. Each unbound pad appears under **Problems** in the outliner. On its **Connections** tab, bind it to a track. The pad surface appears after it is bound. See [Rebind crossing pads](rebinding-crossing-pads.md).
  - The controller stays at the placement point. To move the crossing, remove it and duplicate it again. Move its masts and bell as you would for any crossing.
- A crossing without signals becomes a new crossing with its own crossbucks and crossing pads. If several crossings share a name, as the three below Old River Rd do, the patcher copies the crossing you selected. The whistle signs stay with the original. As with other mod objects, the outliner names the new crossing for its place and gives it a number. Each pad gets its own marker ID and starts with no track binding. It appears under **Problems** until you bind it. Move each crossbuck as you would any other object. The crossing stays where you placed it. To remove the crossing, select it in the outliner and click **Remove modification**.
- A crossing pad becomes a separate object beside its crossing, with its own marker ID and no track binding.
- A spawn point gets an empty **Teleport name** field on the **Object** tab. Enter a name and click **Set**. The name becomes the spawn point's object name in the game. Players can use it with the `/tp` console command and find it in quick search. Put quotation marks around names with spaces, for example `/tp "Bryson Yard"`. The outliner and inspector heading show the name. Without a teleport name, players have to type the full object name to get here. That name is hard to enter. A teleport name cannot contain a slash (`/`). Clear the field and click **Set** to remove the name. Moving the spawn point keeps its name. A mod spawn point ranks below every map spawn point, so a new game does not start there. It keeps the source spawn point's radius.

To end Duplicate without adding an object, click **Cancel**. The window returns to the source object. Closing the Scene Patcher window or reloading the map also ends Duplicate.

If you select another object in the outliner before placing the copy, Scene Patcher asks whether to cancel Duplicate. Click **Cancel Duplicate** to end Duplicate and inspect the selected object, or **Keep duplicating** to return to the blank object window.

## Edit or remove a mod object

Edit a mod object with the supported edits for its family. You can move, rotate, enable, or disable it where those edits are supported. You can duplicate a mod object too; the new object uses the same map object as its source.

To remove a mod object, inspect it and click **Remove modification**. This removes the object and edits made to its parts.

The placement click is one step in the native map editor's history. Undo removes the mod object, and Redo places it again.

## What Save writes

Save writes a modification for the mod object. It records the map object as `source`, with `source.build` identifying its model and build. It also records the mod object's path as `target` and its placement. On each load, the patcher builds the mod object again from its source. If a game update removes the source object, the patcher uses an identical object in the loaded map and writes a warning naming both objects to the player log. See [Mod objects](scene-patch-file-v1.md#mod-objects) in the format reference.

A crossing modification also records the marker IDs of its crossing pads in `partMarkerIds`. Detection you add is recorded in the same modification. Each pad's binding is recorded in its own modification.

A duplicated station map icon appears on the map at its placement point. It is not linked to a station. To add a station, create it in the native map editor.

## If something goes wrong

- **The click places nothing.** Read the message in the blank object window for the reason. If there is no ground under the pointer, left-click the ground.
- **The mod object is missing after a load.** Check the Log for the modification and the reason, such as a source that no longer matches an object in the map. A mod object needs its source or an identical object in the loaded map.
