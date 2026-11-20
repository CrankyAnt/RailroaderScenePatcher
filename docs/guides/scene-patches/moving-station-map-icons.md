<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a station map icon

This guide shows how to move a station map icon so that the map shows the station where it now stands, and how to disable and restore an icon. A station map icon is the icon that the map window draws for a station. The station itself, its track span, its depot house and its station agent are native map data, which you move in the native map editor.

Station map icons are the objects with a `UI.Map.MapIcon` component directly in the `World/Map Labels/Station Icons` folder of the scene. Each icon is listed once in the **Station map icon** family of the outliner, with physical placement and **Enabled**. The `Station Icons` folder itself has no row, and neither has any other object in it. Map icons that the game creates while it runs, such as those of cars and players, are not listed. A station map icon target requires patcher 0.5.0 or later. The native map editor's Save derives the dependency minimum from the scene patch file's content; see [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## Move a station: native map data first, then the icon

The patcher does not couple native map data and scene objects. Each moves only through its own edit, so move them in this order:

1. In the native map editor, move the station's track span, its depot house and its station agent. The depot houses, such as `br-depot`, `an-newport-depot` and `db-depot-asset`, are native Props.
2. Save in the native map editor.
3. In **Scene patches**, move the station map icon to the station's new place, as described in the next section.
4. Save again.

If the station's building group is a scene object, move it in the **Baked scenery group** family, as described in [move baked scenery groups](moving-baked-scenery-groups.md).

## Move the icon in the native map editor

1. Open **Scene patches**, click **Refresh** in the command band, search for the station's name and select its row under **Station map icon** in the outliner. **Select in world** with a click on the icon's marking also selects it, as described in [select world targets](select-world-targets.md). The **Identity** section of the **Object** tab shows the structure `station-map-icon-v1`. If **Supported edits** on the **Object** tab starts with **Not editable:**, the reason follows.
2. Click **Focus** in the command band to see the icon.
3. On the **Placement** tab, in **Numeric** mode, change the position in map coordinates and, if needed, the rotation in degrees. Click **Apply** at the bottom of the tab. You can also drag the icon with the transform gizmo, as described in [use transform gizmos](using-transform-gizmos.md).
4. Open the map window and check that the icon stands at the station's new place.

The icon's object moves at once. Save, Undo, Redo and **Remove modification** in the footer work as described in [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). Removal returns the icon to the placement that the loaded map gives it.

In Railroader 0.6.3, a moved Bryson station icon appeared at its new map position in play. The test did not establish when an already open map refreshes.

## Disable or enable the icon

Click **Disable** at the bottom of the **Object** tab to remove the icon from the map's icon list. **Enable**, **Clear activity**, Undo or removal can return it to that list when the game enables it.

The game's progression switches 13 station map icons on and off with their station at load and when industries change. An explicit disable keeps the icon off the map when progression would switch it on. The **Object** tab and footer show an amber warning. **Enable**, **Clear activity**, Undo or removal returns control to progression. While progression keeps an icon off without your disable, **Enabled** reads `Yes; the game switches it off now`. If the patcher cannot keep the icon disabled through progression on this load, **Disable** is unavailable and its tooltip explains why. See [objects the game switches](scene-patch-file-v1.md#objects-the-game-switches).

## Write the modification by hand

Copy the identity with **Copy ID** in the command band and add a modification to the `modifications` object of `Data/scene-patches.json`, under a key of your choice, as described in [scene patch file v1](scene-patch-file-v1.md):

```json
"move-bryson-icon": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Map Labels/Station Icons/<icon name>",
    "component": "UI.Map.MapIcon",
    "structure": "station-map-icon-v1"
  },
  "placement": {
    "mode": "fixed",
    "position": [0.0, 0.0, 0.0],
    "rotation": [0.0, 0.0, 0.0, 1.0]
  },
  "dependsOn": []
}
```

Use the scene name, path and coordinates from the inspected identity. Add `"active": false` to disable the icon.

## What the patcher checks

Before it touches the icon, the patcher requires that:

- the icon's scene is loaded and the icon stands directly in `World/Map Labels/Station Icons`,
- the icon's own hierarchy has no missing script, no rigidbody and no culler; a culler on one icon makes only that icon **Not editable**,
- the icon's map coordinates can be read.

After each change, it checks that the icon has the mod's placement and activity, that it still stands in the same object, and that the map has the icon registered while it is active with its parents. Any difference fails the modification and restores the icon's placement and activity. If the restoration cannot be verified, the patcher reports incomplete cleanup and refuses further edits of the icon until **Recheck** succeeds or the world reloads.

## Limits

Automated tests check moves, activity and restoration with controlled icons. In Railroader 0.6.3, the Bryson icon moved 300 m east and was absent at its old spot. The explicitly disabled Cochran icon stayed off the map after the Alarka Branch progression milestone and after Save & Quit and reload. Restoration of the moved icon in play and updates while the map is open remain unverified.
