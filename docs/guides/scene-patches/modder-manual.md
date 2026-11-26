<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Edit scene objects in Railroader

Find an existing scene object, make a supported edit, and save the change with your mod. Railroader Scene Patcher is under development and has no official release or mod package.

## Choose where to edit

- Use the native map editor for map data such as track, industry spans, native spawns, and station click spots in Railroader 0.6.3. Change the native data first when it supplies a track or other reference your scene edit needs.
- Open your mod's map definition in the native map editor and use **Scene Patcher** for supported Unity scene objects. The patcher writes to the edited mod, even when an object comes from an inherited map layer. Your mod's native changes reach a save on the base-game map through a map-changing script or a map patch.
- To edit the same scene patch file outside the game, follow [Write a scene patch file](writing-scene-patch-files.md). Use the [v1 reference](scene-patch-file-v1.md) for field definitions and version requirements.

A scene patch file applies to its scene target map and any map that inherits it as a map layer. The editor uses the bottom map layer as the scene target map for a new file. It keeps the scene target map of an existing file. Tests cover the base-game map (`rr/bushnell-whittier`, terrain `rr/nc-bryson`) and maps that inherit it as a map layer. Maps with a different terrain tile set are untested.

## Find the object and check what it uses

1. Open **Scene Patcher** and click **Refresh**. Search the [outliner](inspect-scene-objects.md) by name, path, family, or binding ID, or [select an object in the world](select-world-targets.md). Confirm a world choice before editing it.
2. On the **Object** tab, check **Identity**, **Supported edits**, **Detected problems**, and **Dependencies**. Click **Copy ID** for the scene target map and the object's stored identity. A path or display name alone can be ambiguous. Use the copied identifiers when writing JSON.
3. Check the current placement and observed bindings on **Placement** and **Connections**. An inactive object or a valid connection to the wrong track might need attention even when the **Problems** filter lists nothing. After a native edit, Undo or Redo, click **Refresh** and inspect the object again.

## Choose an operation

Physical placement controls where an object stands. Depending on what the object supports, use [numeric placement](editing-placement-in-the-map-editor.md#set-a-numeric-placement), the [gizmo](using-transform-gizmos.md), a [one-time snap](snapping-to-track.md), or [track following](following-track.md). Track following places an object relative to the track and moves it when the track moves.

A logical binding identifies the track or other resource the game uses. For example, [rebinding a crossing pad](rebinding-crossing-pads.md) regenerates its geometry along the new track. [Repairing construction segments](repairing-construction-segments.md) makes bridge visibility follow the replacement segments.

| What you want to change | Where to start |
| --- | --- |
| Fix a marker registration or a duplicate marker ID | [Track bindings](repairing-track-bindings.md), [replacement marker IDs](repairing-duplicate-marker-ids.md), and [mileposts](repositioning-mileposts.md) |
| Place starting cars or set whether they start derailed | [Starter placement](placing-starter-markers.md) and [guided tasks](completing-guided-tasks.md) |
| Move a board, a bridge, or a group with its colliders, culling, or terrain mask | [Boards](moving-boards.md), [the Ela bridge](placing-bridges.md), and [baked scenery groups and parts](moving-baked-scenery-groups.md) |
| Move a station's map icon or a player spawn point | [Station map icons](moving-station-map-icons.md) and [spawn points](moving-spawn-points.md) |
| Move an existing mast, bell, or crossbuck | [Existing crossing equipment](moving-crossing-equipment.md) and [crossbucks](moving-baked-scenery-groups.md#crossbucks-at-unprotected-crossings) |
| Remove obsolete detection or cover another track at an existing crossing | [Correct coverage](correcting-crossing-coverage.md) or [add detection](adding-crossing-detection.md). Check the approach spans and island for every affected circuit before applying. |
| Add working lights, gates, or a bell to an existing crossing | [Lights and gates](adding-crossing-equipment.md) use eligible devices loaded in the crossing's scene. [Bells](adding-crossing-bells.md) use the bell definition that the game uses at grade crossings. Check the guide's source requirements and confirm the controller relationship. |

## Save, restore or recover

Use native **Undo** and **Redo** for completed edits. Use **Remove modification** to restore the effective underlying state and **Delete** on an addition to remove its owned relationships. Save to keep a removal. See [save, retry and reload](editing-placement-in-the-map-editor.md#save) for partial writes, the dependency in `Info.json`, and the end of native Undo and Redo history on reload.

If an edit fails, read the reason in the footer and click **Guide** for that action. If cleanup is incomplete, use **Recheck** as directed in [retry after a failed edit](editing-placement-in-the-map-editor.md#retry-after-a-failed-edit). For a protected file, open **Session details** and follow [Recover a protected scene patch file](recover-scene-patch-files.md).

For multiplayer testing, install the supplying mod and a compatible patcher on the host and each client, and allow DLL mods on each computer. Each peer applies the scene patch file to its own world. See [application on each peer](scene-patch-file-v1.md#application-on-each-peer) for load, reconnect, and evidence limits.
