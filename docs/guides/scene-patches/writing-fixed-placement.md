<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Write fixed scene placement

This guide explains how to write a v1 scene patch file for fixed placement of an independent board. This capability supports scene objects identified by the `independent-board-v1` structure, with a `Helpers.Culling.RendererCuller`, one circle map mask and no track relationship. The example uses the Sylva location board, `World/Signs/Loc: Sylva Int` in the `BushnellWhittier-MapManager` scene.

An isolated test on Railroader 0.6.2 build 26335745 verified that board's fixed placement, visible rendering, culler membership and mask position after a measured origin shift. The test covered one build and sampled camera distances. [Move a board with its terrain effects](moving-boards.md) explains the board pattern, its terrain effects and failure limits. For the general workflow, see [Write a scene patch file](writing-scene-patch-files.md).

## Add the scene patch file

Create `Data/scene-patches.json` in the mod that contains the map definition. Fixed placement requires patcher version `0.1.0` at minimum, and target-specific capabilities can raise that minimum. The native independent-board pattern raises it to `0.5.0`, except for the legacy Sylva location sign. That sign still uses the pattern checks but does not raise the pattern-based board dependency minimum. You can also make these edits in the native map editor, where Save writes the scene patch file and dependency for you. See [Edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). At runtime the patcher scans loaded mods in load order. In the native map editor it reads only the edited mod's scene patch file.

Save the scene patch file as strict UTF-8 JSON. For `mapId`, use the selected map definition or one of its resolved map layers, normally the bottom map layer. The scene target map cannot be a map patch. If you are editing a map patch, use one of its map layers because the editor refuses the map patch itself. **Copy ID** fills in the scene target map from your mod's scene patch file. If the file does not exist yet, it uses the bottom map layer that the editor would write. See [the scene patch file reference](scene-patch-file-v1.md#scene-patch-file-fields). Keep one scene patch file with the supplying mod so inspection in the editor and runtime application read the same desired state.

## Describe a fixed placement

In the native map editor, open **Scene patches** and click **Refresh**. Find the board under **Independent board** in the outliner and select its row. The **Object** tab shows its identity and dependencies. Click **Copy ID** to copy that JSON. **Focus** moves the native map editor camera without changing the inspected selection. Refresh only inspects; it does not apply scene patches. Add a unique modification ID and dependencies to the copied identity fields. See [scene inspection](inspect-scene-objects.md) for stale observations, dependency limits and the wider inspection-only inventory.

Add one member to the `modifications` object for each desired placement. The member's key is the modification ID. This example shows the v1 fields and the supported target identity. Replace the sample map ID and coordinates with values for the selected map.

```json
{
  "formatVersion": 1,
  "sourceMap": {
    "mapId": "example-map-id"
  },
  "modifications": {
    "move-example-object": {
      "target": {
        "scene": "BushnellWhittier-MapManager",
        "path": "World/Signs/Loc: Sylva Int",
        "component": "Helpers.Culling.RendererCuller",
        "structure": "independent-board-v1"
      },
      "placement": {
        "mode": "fixed",
        "position": [12.0, 2.0, 3.0],
        "rotation": [0.0, 0.0, 0.0, 1.0]
      },
      "dependsOn": []
    }
  }
}
```

The target identity must identify exactly one board in the loaded map context that matches the board pattern. The position has three finite numbers in map coordinates. The rotation has four finite numbers in quaternion order and must be a unit quaternion. The patcher uses the game's coordinate conversion to turn map coordinates into world coordinates. After moving the board, it updates the culling position and rebuilds the circle map mask.

Give each modification a unique, non-empty key. A modification has no `id` member, and the order of the keys has no meaning. List prerequisite modification IDs in `dependsOn`, or leave it empty if there are no prerequisites. A missing, failed, or cyclic dependency prevents the dependent modification from applying.

## Distribute for multiplayer

Every player in a multiplayer session needs your mod and Railroader Scene Patcher installed, with the game's DLL mod permission enabled on their own installation. Railroader's checks of required mods and versions decide who can join. The patcher adds no admission check of its own and does not compare scene patch file contents between players.

The host and each client apply your scene patch file to their own scene when their world loads. Each checks the map context, target identity and capability prerequisites locally. A failure in one player's game is reported there and does not undo changes for the others or disconnect anyone. Fixed placement uses map coordinates, so each player's board ends up at the same map position even though their local world origins differ.

When a player's game loads the world again, the patcher uses the stored map position. If the game rebuilt an object, the patcher applies your modification to it as new and removes anything it built for the old object. In single-player, loading the same save again rebuilt the objects and applied each modification once. See [application on each peer](scene-patch-file-v1.md#application-on-each-peer) for repeat application, failure handling and multiplayer evidence limits.

## Check the support boundary

The board's parents, culler and circle map mask must be active. The board itself may be disabled through `active`. The patcher rejects a height-flattening mask, `Track.*` components and other culling or mask components in the hierarchy. It also checks that the required game functions for map-coordinate conversion, culling, masks and terrain are available. For the supported bridge, see [move or disable a bridge with its collision](placing-bridges.md). The [Current native targets](scene-patch-file-v1.md#current-native-targets) section lists all supported object families. Families not listed there are not implemented. The runtime reports `Capability.Unchecked` even when these prerequisites are available. The isolated fixture observations described above provide evidence for the tested board and build; they do not change that capability classification or establish the complete culling range.

If a modification fails during a load, the engine does not retry it during that load. Correct the scene patch file or target before a later load.

If you correct a scene patch file outside the editing session, reload the map before retrying. For the exact v1 field requirements, see [scene patch file v1](scene-patch-file-v1.md).
