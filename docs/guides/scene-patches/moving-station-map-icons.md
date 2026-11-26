<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a station map icon

Move a station map icon to show the station's new location in the map window. You can also disable and restore it. The station itself, its track span, its depot house, and its station agent are native map data. Move those in the native map editor.

Station map icons are the direct children with a `UI.Map.MapIcon` component under the `World/Map Labels/Station Icons` object, which holds the icons and has no row of its own. Each icon is listed once under the **Station map icon** category of the outliner, with physical placement and **Enabled**. Only the icons directly in that object get rows. Map icons that the game creates while it runs, such as those of cars and players, are not listed. A station map icon target requires patcher 0.5.0 or later. The native map editor's Save derives the dependency minimum from the scene patch file's content; see [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## Move a station: native map data first, then the icon

Moving native map data doesn't move the corresponding scene objects, or vice versa. Move them in this order:

1. In the native map editor, move the station's track span, its depot house, and its station agent. The depot houses, such as `br-depot`, `an-newport-depot`, and `db-depot-asset`, are native Props.
2. Save in the native map editor.
3. In **Scene Patcher**, move the station map icon to the station's new place, as described in the next section.
4. Save again.

If the station's building group is a scene object, move it as a group, as described in [groups and parts](moving-scene-objects.md).

## Move the icon in the native map editor

1. Open **Scene Patcher**, click **Refresh** in the command band, search for the station's name, and select its row under **Station map icon** in the outliner. **Select in world** with a click on the icon's marking also selects it, as described in [select world targets](select-world-targets.md). The **Identity** section of the **Object** tab shows the structure `station-map-icon-v1`. If **Supported edits** on the **Object** tab lists the icon's edits under **Unavailable:**, the reason on that line follows.
2. Click **Focus** in the command band to see the icon.
3. On the **Placement** tab, in **Numeric** mode, change the position in map coordinates and, if needed, the rotation in degrees. Click **Apply** at the bottom of the tab. You can also drag the icon with the transform gizmo, as described in [use transform gizmos](using-transform-gizmos.md).
4. Open the map window and check that the icon stands at the station's new place.

The scene object for the icon moves immediately. Save, Undo, Redo, and **Remove modification** in the footer work as described in [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). Removal restores the icon's placement from the loaded map.

In Railroader 0.6.3, a moved Bryson station icon appeared at its new map position in play. The test did not establish when an already open map refreshes.

## Disable or enable the icon

Click **Disable** at the bottom of the **Object** tab to remove the icon from the map's icon list. **Enable**, **Clear activity**, Undo, or removal can return it to that list when the game enables it.

The game's progression switches 13 station map icons on and off with their station at load and when industries change. An explicit disable keeps the icon off the map when progression would switch it on. The **Object** tab and footer show an amber warning. **Enable**, **Clear activity**, Undo, or removal returns control to progression. While progression keeps an icon off without your disable, **Enabled** reads `Yes, but currently switched off by the game`. If the patcher cannot keep the icon disabled through progression on this load, **Disable** is unavailable and its tooltip explains why. See [objects the game switches](scene-patch-file-v1.md#objects-the-game-switches).

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

Use the scene name, path, and coordinates from the inspected identity. Add `"active": false` to disable the icon.

## What the patcher checks

Before changing an icon, the patcher checks that:

- the icon's scene is loaded and the icon stands directly in `World/Map Labels/Station Icons`,
- the icon's own hierarchy has no missing script, no rigidbody, and no culler; a culler on one icon makes only that icon **Not editable**,
- the icon's map coordinates can be read.

After each change, the patcher checks the icon's placement and activity against the mod values and confirms that it still belongs to the same object. It also checks that the map registers the icon while both the icon and its parents are active. If any check finds a difference, the modification fails and the patcher restores the previous placement and activity. If it can't verify restoration, it reports incomplete cleanup. Further edits to the icon stay blocked until **Recheck** succeeds or the world reloads.

## Limits

Automated tests check moves, activity, and restoration with controlled icons. In Railroader 0.6.3, the Bryson icon moved 300 m east and was absent at its old spot. The explicitly disabled Cochran icon stayed off the map after the Alarka Branch progression milestone and after Save & Quit and reload. Restoration of the moved icon in play and updates while the map is open remain unverified.
