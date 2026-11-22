<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move player spawn points

Move, rotate, disable, or restore a scene spawn point to choose where players start, teleport, and arrive after rescue on your map. For the general native map editor workflow, including Save, Undo, and Redo, see [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). For the JSON fields, see [scene patch file v1](scene-patch-file-v1.md#spawn-point).

## Scene spawn points and native spawns

A scene spawn point is a scene object with a `Character.SpawnPoint` component. The base-game map has 22 scene spawn points under `World/POIs`, with names such as `Alarka` or `Bryson` for their locations. The outliner lists each one once in the **Spawn point** family.

A native spawn is an entry in the `spawns` section of a map's `Map.json`. The native map editor edits native spawns in its **Spawns** category. The patcher does not list or change them. The base map declares no native spawns.

## What the game uses a spawn point for

The **Use in the game** section of the **Object** tab lists the uses of the inspected spawn point. Use the list below to choose what to check in a test save.

- **New game:** when the map declares no native spawns, a new game without a start setup is expected to start at the active spawn point with the highest priority. If several active spawn points share that priority, the game doesn't specify which it chooses, and the tab says so. If the map has native spawns, the new game is expected to use those instead.
- **Rescue:** a stuck player is expected to be moved to the closest active spawn point.
- **Teleport:** the `/tp` console command is expected to offer each active spawn point by its object name and jump to where it stands.
- **Start setup:** when the game applies an active start setup to a company with no cars and no balance yet, the new company game is expected to start at the spawn point that setup names, even if the point is disabled. The tab names each such setup by its identifier, or by its object name if it has no identifier. It hasn't been established whether a start setup or the map's native spawns take precedence when both exist.

The game reads a spawn point's transform each time it uses it. In Railroader 0.6.3, `/tp alarka` took the player to the moved Alarka point. A new East Whittier company game without native spawns used the moved spawn point named by its start setup. Removing the modification returned the new company's start to its authored position.

A new game reads its start spawn point while the map loads, before the patcher applies the rest of the scene patch file. The patcher therefore applies spawn point modifications first, just before the game reads those points. A spawn point modification that depends on another modification or uses track-following placement waits for the normal world-load application. In that case, the new game starts at the spawn point's position on the loaded map.

## Move a spawn point

1. Open **Scene patches**, click **Refresh** in the command band, open the **Spawn point** family, and select the spawn point's row in the outliner. The **Identity** section of the **Object** tab shows the structure `spawn-point-v1`. If **Supported edits** starts with **Not editable:**, the reason follows.
2. Click **Focus** in the command band. A spawn point has no model, so it shows a [helper marker](select-world-targets.md#helper-markers-for-invisible-objects): a `+` on its pivot.
3. On the **Placement** tab, enter the position in map coordinates and the rotation in degrees in **Numeric** mode and click **Apply**, or drag the [transform gizmo](using-transform-gizmos.md).

The spawn point moves at once, and its helper marker moves with it. Save, Undo, Redo, and **Remove modification** in the footer work as for other objects. Removal returns the spawn point to the placement the loaded map gives it.

## Disable or enable a spawn point

Click **Disable** at the bottom of the **Object** tab to take the spawn point out of rescue, teleport, and the default start. Click **Enable**, Undo, or **Remove modification** to bring it back.

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

Before a change, the patcher checks that the spawn point's scene is loaded and its path names exactly one spawn point. After the change, it checks that the spawn point has the mod's placement and activity. If either differs, the modification fails and the patcher restores the spawn point's placement and activity. If it can't verify restoration, it reports incomplete cleanup and refuses further edits to the spawn point until **Recheck** succeeds or the world reloads.

## Limits

Automated tests check placement and activity with a controlled spawn point. The Railroader 0.6.3 runs did not establish rescue, the default start without a start setup, the behavior of an inactive point named by a start setup, or precedence when a map also supplies native spawns. A baked scenery group that holds a spawn point cannot be moved as a whole. Edit each spawn point in its own family.
