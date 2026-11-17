<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a baked scenery group

This guide shows how to move, rotate, disable and restore a whole baked scenery group, such as a platform, a lamp, a sign or a small building that the native map editor does not edit, and a single part inside such a group, such as a jetty at a sawmill or a lamp at a station. A group move is physical placement only: everything below the group root moves with it, culling and colliders follow, and the activity of the parts stays as it was. A part edit changes that part and what stands below it; the group root and the other parts stay where they are. Moving or disabling a group or a part changes no game function.

A baked scenery group is saved with the map's scene. Props, masks and other objects of the map's `Map.json` are not baked scenery groups; the native map editor edits those. A baked scenery group target requires patcher 0.5.0 or later. The native map editor's Save derives the dependency minimum from the scene patch file's content; see [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## Which groups are listed

The **Baked scenery group** family in the outliner lists one entry per group root. A group root is a scene object one level below a folder of `World`, such as `World/Roads Barkers/1932 Chevy Truck LP`, and below `World/Large Scenery` one level below a town folder, such as `World/Large Scenery/Bryson/Bryson House Track`. A group is listed when the scene saves it, no other family lists it, and it holds at least one renderer, collider or light. An object without any of those, such as a spawn point under `World/POIs`, is not listed; the fireflies under `World/Environment Effects` are visual effects and are not listed either.

A group holds only what the scene saves below its root. Props, tanks, conveyors and terrain masks of the map's `Map.json` that stand beside a group are native map data: the native map editor moves them, and a group move leaves them where they are. The coal and oil tanks beside the lamp post `Bryson Oil` and the conveyor beside the coal pile `Coal & Oil` at Andrews are such Props.

Groups that hold game logic or terrain shaping wait for their own support. They stand in the **Inspection only** group with the reason, for example a group that holds a station click spot, a scene ground mask, a car loader, a lake surface, a log pond, storage piles, a spawn point, a map icon or a pickable toggle, and road bridges and rail bridge constructions outside Ela. A group that holds track markers stands there as well: the markers keep their own family, and moving such a group as a whole is not supported yet. Crossbucks at unprotected crossings stand there too. A group in the **Inspection only** group lists no parts.

## Which parts are listed

Each group in the **Baked scenery group** family lists its parts as entries of the same family; in name order they follow the group's own row. A part entry is named with its group: the group root's name, then the path from the root to the part, such as `Whittier Saw Mill / Jetty / Piece (2)`. A part is one scene object at any depth below the group root, not a renderer or a component, and it is listed when the scene saves it and it holds a renderer, collider or light at or below it. A part can hold other parts; moving it moves them with it.

Some objects below a group root are not listed as parts:

- Two or more sibling objects with the same name under the same parent share one path. A scene patch file could not tell them apart, so neither they nor the objects below them are listed.
- A level of a LOD group, such as `LOD0` and `LOD1` below a lamp, belongs to the entry of the object that holds the LOD group.
- An object without a renderer, collider or light, an object the game creates while running, and an object that another family lists, are not listed.

## Move a group in the native map editor

1. Open **Scene patches**, select **Refresh** in the command band, search for the group, for example `Bryson House Track`, and select its row under **Baked scenery group**. **Select in world** with a click on the group's marking also selects it, as described in [select world targets](select-world-targets.md). The **Identity** section of the **Object** tab shows the component `UnityEngine.Transform` and the structure `baked-scenery-group-v1`, and **Use in the game** says that the group is decorative.
2. Select **Focus** in the command band to see the group.
3. On the **Placement** tab, change the six **Numeric** fields, position in map coordinates and rotation in degrees, and select **Apply** at the bottom of the tab. **Gizmo** and **Snap** work as for other objects; see [using transform gizmos](using-transform-gizmos.md) and [snapping to track](snapping-to-track.md).

The group root moves at once with everything below it. Save, Undo, Redo and **Remove modification** in the footer work as described in [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). Removal returns the group to the placement the loaded map gives it, together with the values other mods still set.

If **Supported edits** on the **Object** tab starts with **Not editable:**, the reason follows, for example a hierarchy path that two groups share.

## Disable or enable a group

Select **Disable** at the bottom of the **Object** tab. The group root is switched off, so the group disappears and its colliders stop colliding. The parts keep their own **Enabled** settings, so **Enable**, Undo or removal brings back the same group. A group whose root carries a simple culler, such as Bryson House Track, stays off while the camera comes near: the culler's switching pauses while the group is disabled and resumes when it is enabled. Enabled again, such a group shows only when its culler counts it as in view, as before it was disabled.

## Move or disable a single part

1. Select the part's row under **Baked scenery group**, for example `Bryson House Track / Ramp`. The **Identity** section of the **Object** tab shows the component `UnityEngine.Transform` and the structure `baked-scenery-part-v1`, and **Use in the game** says that the part is decorative.
2. On the **Placement** tab, change the **Numeric** fields and select **Apply**, or use **Gizmo** or **Snap** as for other objects. Select **Disable** at the bottom of the **Object** tab to switch the part off.

A part with a fixed placement keeps that placement in map coordinates when its group moves later. This holds for a group move in the same scene patch file or in another mod's, in either key order or load order, and during a **Gizmo** drag of the group. The group's placement applies first, then the placement of each part with a fixed placement. A part without a fixed placement moves with its group.

**Remove modification** on a part returns it to the place it has without the modification relative to the object directly above it, the group root or the part it stands below, wherever that object stands now. The part then moves with that object again. A part below a moved part keeps its place on that part, also when you disable and enable it or remove its own modification.

**Disable** stays unavailable, with the reason, for a part that a culler of the game switches on and off by camera distance. The culler would override a stored disable, so the patcher cannot hold it. A placement edit of such a part leaves it on or off as the culler switched it.

## Write the modification by hand

Copy the identity with **Copy ID** in the command band and add a modification to the `modifications` object of `Data/scene-patches.json`, under a key of your choice, as described in [scene patch file v1](scene-patch-file-v1.md):

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

A part modification names the part's path and the structure `baked-scenery-part-v1`; copy it with **Copy ID** on the part's row:

```json
"move-saw-mill-jetty": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Whittier/Whittier Saw Mill/Jetty",
    "component": "UnityEngine.Transform",
    "structure": "baked-scenery-part-v1"
  },
  "placement": {
    "mode": "fixed",
    "position": [0.0, 0.0, 0.0],
    "rotation": [0.0, 0.0, 0.0, 1.0]
  },
  "dependsOn": []
}
```

The placement is in map coordinates, not relative to the group.

## What the patcher checks

Before it touches a group, the patcher requires that the path names exactly one listed group root, that its scene is loaded, and that the group holds no missing script and no rigidbody. A path that names no group or several groups fails that modification and leaves the others to apply.

For a part it requires that the path names exactly one listed part and that its group passes the same checks.

After each group change it checks that the root has the mod's placement and activity, that every part kept its place relative to the root and, unless a culler switches it, its own activity, and that the culling sphere of every enabled culler in the group stands at its culler. A part with a fixed placement must stay at that placement instead. A disabled group holds no culling sphere. After each part change it checks the part's placement and activity, that the group root and every other part kept their placement and activity, and that what stands below the part kept its place relative to it. While a part keeps a fixed placement of its own, every culler above it takes a radius that encloses the renderers it culls, and never one below the radius the scene gives it: the culler on the group root, a culler on a part above the held part, and a game-object culler that switches the held part or an object above it. Without a held part below it, a culler has the radius the scene gives it again. The culler on a moved part moves with it. Any difference fails the modification and restores the group or the part. If the restoration cannot be verified, the patcher reports incomplete cleanup and refuses further edits of the group or part until **Recheck** succeeds or the world reloads.

## Limits

Grass and other terrain detail under a group come from the terrain and its masks. A native mask of the map, such as `br-house-track` under Bryson House Track, keeps the ground clear at the group's original place; after a move, grass grows through the group at its new place until you move or add the mask in the native map editor.

The automated tests check this behavior with a controlled group. Game runs on Railroader 0.6.2 build 26335745 observed the following on the base map. Bryson House Track, Bryson Oil, Coal & Oil at Andrews and the inactive Alarka/Platform render at their new place at near distance and at 220 to 450 m, and Bryson Yard moves with all its posts and lamps; no part stayed at the old place. A player character stands on the moved Bryson House Track platform and is stopped at its edge, walks freely through its old place, and meets neither place while the group is disabled. A disabled group with a simple culler on its root stays off when the camera comes near, and after Enable it shows only when the camera comes near again. After a camera jump that shifted the floating origin, a moved group kept its map position, and the saved position holds no origin offset.

Whether every culling sphere of a moved group encloses its renderers has not been measured. Other groups, groups with game logic or terrain shaping and multiplayer are not covered.

The automated tests check part edits with a controlled group. A part edit has not yet been observed in a game run.
