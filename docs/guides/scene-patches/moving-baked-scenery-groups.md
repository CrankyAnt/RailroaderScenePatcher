<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a baked scenery group

This guide shows how to move, rotate, disable and restore a whole baked scenery group, such as a platform, a lamp, a sign or a small building that the native map editor does not edit. A group move is physical placement only: everything below the group root moves with it, culling and colliders follow, and the activity of the parts stays as it was. Moving or disabling one of these groups changes no game function.

A baked scenery group is saved with the map's scene. Props, masks and other objects of the map's `Map.json` are not baked scenery groups; the native map editor edits those. A baked scenery group target requires patcher 0.5.0 or later. The native map editor's Save derives the dependency minimum from the document's content; see [the document reference](patch-document-v1.md#dependency-minimum).

## Which groups are listed

The **Baked scenery group** family in the outliner lists one entry per group root. A group root is a scene object one level below a folder of `World`, such as `World/Roads Barkers/1932 Chevy Truck LP`, and below `World/Large Scenery` one level below a town folder, such as `World/Large Scenery/Bryson/Bryson House Track`. A group is listed when the scene saves it, no other family lists it, and it holds at least one renderer, collider or light. An object without any of those, such as a spawn point under `World/POIs`, is not listed; the fireflies under `World/Environment Effects` are visual effects and are not listed either.

A group holds only what the scene saves below its root. Props, tanks, conveyors and terrain masks of the map's `Map.json` that stand beside a group are native map data: the native map editor moves them, and a group move leaves them where they are. The coal and oil tanks beside the lamp post `Bryson Oil` and the conveyor beside the coal pile `Coal & Oil` at Andrews are such Props.

Groups that hold game logic or terrain shaping wait for their own support. They stand in the **Inspection only** group with the reason, for example a group that holds a station click spot, a scene ground mask, a car loader, a lake surface, a log pond, storage piles, a spawn point, a map icon or a pickable toggle, and road bridges and rail bridge constructions outside Ela. A group that holds track markers stands there as well: the markers keep their own family, and moving such a group as a whole is not supported yet. Crossbucks at unprotected crossings stand there too. Single parts inside a group are not listed on their own yet.

## Move a group in the native map editor

1. Open **Scene patches**, select **Refresh** in the command band, search for the group, for example `Bryson House Track`, and select its row under **Baked scenery group**. **Select in world** with a click on the group's marking also selects it, as described in [select world targets](select-world-targets.md). The **Identity** section of the **Object** tab shows the component `UnityEngine.Transform` and the structure `baked-scenery-group-v1`, and **Use in the game** says that the group is decorative.
2. Select **Focus** in the command band to see the group.
3. On the **Placement** tab, change the six **Numeric** fields, position in map coordinates and rotation in degrees, and select **Apply** at the bottom of the tab. **Gizmo** and **Snap** work as for other objects; see [using transform gizmos](using-transform-gizmos.md) and [snapping to track](snapping-to-track.md).

The group root moves at once with everything below it. Save, Undo, Redo and **Remove modification** in the footer work as described in [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). Removal returns the group to the placement the loaded map gives it, together with the values other mods still set.

If **Supported edits** on the **Object** tab starts with **Not editable:**, the reason follows, for example a hierarchy path that two groups share.

## Disable or enable a group

Select **Disable** at the bottom of the **Object** tab. The group root is switched off, so the group disappears and its colliders stop colliding. The parts keep their own **Enabled** settings, so **Enable**, Undo or removal brings back the same group. A group whose root carries a simple culler, such as Bryson House Track, stays off while the camera comes near: the culler's switching pauses while the group is disabled and resumes when it is enabled. Enabled again, such a group shows only when its culler counts it as in view, as before it was disabled.

## Write the modification by hand

Copy the identity with **Copy ID** in the command band and add a modification to the `modifications` object of `Data/scene-patches.json`, under a key of your choice, as described in [patch document v1](patch-document-v1.md):

```json
"move-bryson-house-track": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Bryson/Bryson House Track",
    "component": "UnityEngine.Transform",
    "structure": "baked-scenery-group-v1"
  },
  "placement": {
    "mode": "fixed",
    "position": [0.0, 0.0, 0.0],
    "rotation": [0.0, 0.0, 0.0, 1.0]
  },
  "dependsOn": []
}
```

Use the scene name and coordinates from the inspected identity. Add `"active": false` to disable the group.

## What the patcher checks

Before it touches a group, the patcher requires that the path names exactly one listed group root, that its scene is loaded, and that the group holds no missing script and no rigidbody. A path that names no group or several groups fails that modification and leaves the others to apply.

After each change it checks that the root has the mod's placement and activity, that every part kept its place relative to the root and, unless a culler switches it, its own activity, and that the culling sphere of every enabled culler in the group stands at its culler. A disabled group holds no culling sphere. Any difference fails the modification and restores the group. If the restoration cannot be verified, the patcher reports incomplete cleanup and refuses further edits of the group until **Recheck** succeeds or the world reloads.

## Limits

Grass and other terrain detail under a group come from the terrain and its masks. A native mask of the map, such as `br-house-track` under Bryson House Track, keeps the ground clear at the group's original place; after a move, grass grows through the group at its new place until you move or add the mask in the native map editor.

The automated tests check this behavior with a controlled group. Rendering, culling at near and far camera distance, collision at the new place and the conversion between map coordinates and the moving origin have not yet been observed in the game for this build. Single parts, groups with game logic or terrain shaping and multiplayer are not covered.
