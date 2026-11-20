<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move player spawn points

This guide shows how to move, rotate, disable or restore a scene spawn point, so that players start, teleport and are rescued at places that fit your map. For the general native map editor workflow, Save, Undo and Redo, see [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). For the JSON fields, see [scene patch file v1](scene-patch-file-v1.md#spawn-point).

## Scene spawn points and native spawns

A scene spawn point is a scene object with a `Character.SpawnPoint` component, such as the 22 objects under `World/POIs` on the base map, named after the places they stand at, like `Alarka` or `Bryson`. The outliner lists each one once in the **Spawn point** family.

A native spawn is an entry in the `spawns` section of a map's `Map.json`. The native map editor edits native spawns in its **Spawns** category. The patcher does not list or change them. The base map declares no native spawns.

## What the game uses a spawn point for

The **Use in the game** section of the **Object** tab names each use for the inspected spawn point. These uses come from the game's code and have not yet been observed in Railroader, so the tab and this guide describe them as expected behavior:

- **New game:** when the map declares no native spawns, a new game without a start setup is expected to start at the active spawn point with the highest priority. If several active spawn points share that priority, the game does not fix which of them it takes, and the tab says so. With native spawns, the new game is expected to start at those instead.
- **Rescue:** a stuck player is expected to be moved to the closest active spawn point.
- **Teleport and quick search:** the `/tp` console command and quick search are expected to offer each active spawn point by its object name and jump to where it stands.
- **Start setup:** when the game applies an active start setup that names the spawn point to a company with no cars and no balance yet, the new company game is expected to start there, also while the spawn point is disabled. The tab names each such setup by its identifier, or by its object name when it has none. Whether a start setup or the map's native spawns win when both exist has not been established.

The game reads a spawn point where it stands each time it uses it, so a moved spawn point is expected to be used at its new place. A disabled spawn point is expected to be passed by for rescue, teleport, quick search and the default start, and the tab says so for a disabled spawn point. An inactive spawn point is not a problem.

A new game reads its start spawn point while the map loads, before the patcher applies the rest of the scene patch file. So when a new game starts, the patcher applies the spawn point modifications first, just before the game reads them. A spawn point modification that depends on another modification, or uses a track-following placement, waits for the normal world-load application. A new game then starts at the spawn point's place on the loaded map.

## Move a spawn point

1. Open **Scene patches**, click **Refresh** in the command band, open the **Spawn point** family and select the spawn point's row in the outliner. The **Identity** section of the **Object** tab shows the structure `spawn-point-v1`. If **Supported edits** starts with **Not editable:**, the reason follows.
2. Click **Focus** in the command band. A spawn point has no model, so it shows a [helper marker](select-world-targets.md#helper-markers-for-invisible-objects): a `+` on its pivot.
3. On the **Placement** tab, enter the position in map coordinates and the rotation in degrees in **Numeric** mode and click **Apply**, or drag the [transform gizmo](using-transform-gizmos.md).

The spawn point moves at once, and its helper marker moves with it. Save, Undo, Redo and **Remove modification** in the footer work as for other objects. Removal returns the spawn point to the placement the loaded map gives it.

## Disable or enable a spawn point

Click **Disable** at the bottom of the **Object** tab to take the spawn point out of rescue, teleport, quick search and the default start. Click **Enable**, Undo, or **Remove modification** to bring it back.

## Write the modification by hand

Copy the identity with **Copy ID** in the command band and add a modification to the `modifications` object of `Data/scene-patches.json`:

```json
"move-alarka-spawn": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/POIs/Alarka",
    "component": "Character.SpawnPoint",
    "structure": "spawn-point-v1"
  },
  "placement": {
    "mode": "fixed",
    "position": [0.0, 0.0, 0.0],
    "rotation": [0.0, 0.0, 0.0, 1.0]
  },
  "dependsOn": []
}
```

Use the scene name and coordinates from the inspected identity. Add `"active": false` to disable the spawn point. A spawn point target requires patcher 0.5.0 or later; the native map editor's Save raises the dependency minimum in `Info.json`.

## What the patcher checks

Before a change, the patcher requires that the spawn point's scene is loaded and that its path names exactly one spawn point. After the change, it checks that the spawn point has the mod's placement and activity. A difference fails the modification and restores the spawn point's placement and activity. If that restoration cannot be verified, the patcher reports incomplete cleanup and refuses further edits of the spawn point until **Recheck** succeeds or the world reloads.

## Limits

The automated tests check this behavior with a controlled spawn point. Start, rescue, teleport and quick search at a moved spawn point have not yet been observed in Railroader. If you disable every active spawn point on a map without native spawns, a new game without a start setup has no spawn point to start at; the patcher does not warn about this. A baked scenery group that holds a spawn point is not moved as a whole; edit each spawn point in its own family.
