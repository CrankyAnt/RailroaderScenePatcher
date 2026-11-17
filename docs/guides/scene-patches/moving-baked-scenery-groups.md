<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a baked scenery group

This guide covers moving, rotating, disabling and restoring baked scenery groups and their individual parts. Groups include platforms, lamps, signs and small buildings that the native map editor does not edit. A part might be a sawmill jetty or a station lamp. Moving a group changes its physical placement: everything below the group root moves with it, culling and colliders follow, and each part keeps its activity. Editing a part affects it and the objects below it; the group root and other parts stay where they are. Moving or disabling a group or part changes no game function.

A baked scenery group is saved with the map's scene. Props, masks and other objects of the map's `Map.json` are not baked scenery groups; the native map editor edits those. A baked scenery group target requires patcher 0.5.0 or later. The native map editor's Save derives the dependency minimum from the scene patch file's content; see [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## Which groups are listed

The **Baked scenery group** family in the outliner lists one entry per group root. A group root is a scene object one level below a `World` folder, such as `World/Roads Barkers/1932 Chevy Truck LP`. Under `World/Large Scenery`, it is one level below a town folder, such as `World/Large Scenery/Bryson/Bryson House Track`. A group is listed when the scene saves it, no other family lists it, and it has a renderer, collider or light at or below its root. An object without any of those, such as a spawn point under `World/POIs`, is not listed; the fireflies under `World/Environment Effects` are visual effects and are not listed either.

A group contains only what the scene saves below its root. Props, tanks, conveyors and terrain masks in the map's `Map.json` may stand beside a group, but they are native map data. The native map editor moves them; moving the group leaves them in place. The coal and oil tanks beside the lamp post `Bryson Oil` and the conveyor beside the coal pile `Coal & Oil` at Andrews are such Props.

Groups containing game logic or terrain shaping need separate support. They appear under **Inspection only** with the reason. Examples include groups containing a station click spot, scene ground mask, car loader, lake surface, log pond, storage piles, spawn point, map icon or pickable toggle. Road bridges and rail bridge constructions outside Ela also appear there. So do groups containing track markers: the markers keep their own family, and moving the whole group is not yet supported. Crossbucks at unprotected crossings also appear under **Inspection only**. Groups there list no parts.

## Which parts are listed

Each group in the **Baked scenery group** family lists its parts as entries in the same family. In name order, they follow the group's row. Each name combines the group root's name with the path to the part, for example `Whittier Saw Mill / Jetty / Piece (2)`. A part is a scene object at any depth below the root, rather than a renderer or component. It is listed when the scene saves it and it has a renderer, collider or light at or below it. A part can contain other parts, which move with it.

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

Select **Disable** at the bottom of the **Object** tab. This switches off the group root, hides the group and stops its colliders from colliding. Parts keep their own **Enabled** settings, so **Enable**, Undo or removal restores the same group. If the root has a simple culler, as at Bryson House Track, the culler stops switching it while the group is disabled. The group stays off even when the camera comes near. Once enabled, the culler resumes switching it, and the group appears only when the culler considers it in view, as before.

## Move or disable a single part

1. Select the part's row under **Baked scenery group**, for example `Bryson House Track / Ramp`. The **Identity** section of the **Object** tab shows the component `UnityEngine.Transform` and the structure `baked-scenery-part-v1`, and **Use in the game** says that the part is decorative.
2. On the **Placement** tab, change the **Numeric** fields and select **Apply**, or use **Gizmo** or **Snap** as for other objects. Select **Disable** at the bottom of the **Object** tab to switch the part off.

A part with a fixed placement keeps its map coordinates when its group moves later, including during a **Gizmo** drag. This applies whether the group move is in the same scene patch file or another mod's, regardless of key or load order. The group placement applies first, followed by each fixed part placement. A part without a fixed placement moves with its group.

**Remove modification** returns a part to its unmodified placement relative to the object directly above it, whether that is the group root or another part, at that object's current placement. The part then moves with that object again. A part below a moved part keeps its place on that part when you disable and enable it or remove its own modification.

**Disable** is unavailable for a part that a game culler switches on and off by camera distance; the editor shows the reason. The culler would override a stored disable, so the patcher cannot maintain it. A placement edit leaves the part in the state set by the culler.

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

The part placement uses map coordinates, not coordinates relative to the group.

## What the patcher checks

Before it touches a group, the patcher requires that the path names exactly one listed group root, that its scene is loaded, and that the group holds no missing script and no rigidbody. A path that names no group or several groups fails that modification and leaves the others to apply.

For a part, the path must name exactly one listed part and its group must pass the same checks.

After a group change, the patcher checks the root's placement and activity against the mod. It checks that every part kept its place relative to the root, except for parts with their own fixed placement, which must remain at that placement. It also checks each part's own activity unless a culler switches it, and checks that every enabled culler's sphere stands at its culler. A disabled group holds no culling sphere.

After a part change, the patcher checks the part's placement and activity. It also checks that the group root and every other part kept their placement and activity, and that objects below the part kept their placement relative to it. A culler on a moved part moves with it.

While a part has its own fixed placement, each culler above it, on the group root or a part above the fixed part, takes a radius that encloses the renderers it culls. The radius never falls below the radius the scene gives it. A renderer counts when it is on as the scene has it, including when a culler hides it because the camera is far away. A game-object culler culls the objects it switches, not the objects below it. It grows only while a fixed part is one of those objects, is below or above one of them, or carries the culler. Without such a fixed part, a culler returns to the radius the scene gives it.

Any difference fails the modification and restores the group or part. If the patcher cannot verify restoration, it reports incomplete cleanup and refuses further edits of that group or part until **Recheck** succeeds or the world reloads.

## Limits

Grass and other terrain detail under a group come from the terrain and its masks. A native map mask, such as `br-house-track` under Bryson House Track, keeps the ground clear at the group's original position. After a move, grass grows through the group at its new position until you move or add the mask in the native map editor.

Automated tests check group behavior with a controlled group. Runs on the base map in Railroader 0.6.2 build 26335745 observed Bryson House Track, Bryson Oil, Coal & Oil at Andrews and the inactive Alarka/Platform render at their new positions both nearby and at 220 to 450 m. Bryson Yard moved with all its posts and lamps; no part remained at its old position. The player character stood on the moved Bryson House Track platform and was stopped at its edge, walked freely through its old position, and encountered neither position while the group was disabled. A disabled group with a simple culler on its root stayed off when the camera came near. After Enable, it appeared only when the camera came near again. After a camera jump shifted the floating origin, a moved group kept its map position and the saved position had no origin offset.

The runs did not measure whether every culling sphere of a moved group encloses its renderers. They did not cover other groups, groups with game logic or terrain shaping, or multiplayer.

The automated tests check part edits with a controlled group. A part edit has not yet been observed in a game run.
