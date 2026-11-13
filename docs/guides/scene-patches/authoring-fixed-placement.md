<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Author fixed scene placement

This guide describes the v1 scene-patch document layout and the native placement capability for independent boards: scene objects with a `Helpers.Culling.RendererCuller`, one circle map mask and no track relationship, identified by the `independent-board-v1` structure. The example uses the Sylva location board, `World/Signs/Loc: Sylva Int` in the `BushnellWhittier-MapManager` scene. An isolated test on Railroader 0.6.2 build 26335745 verified that board's fixed pose, visible rendering, culler membership and mask position after a measured origin shift. That test does not establish other builds or the complete culling range. [Move a board with its terrain effects](moving-boards.md) explains the board pattern, its terrain effects and failure limits.

## Add the patch document

Create `Data/scene-patches.json` in the asset pack that supplies the Map definition. Fixed placement has a base minimum of `0.1.0`; target-specific capabilities can raise it. The native independent-board pattern raises the minimum to `0.5.0` except for the legacy Sylva location sign, which still uses the pattern checks but does not raise the pattern-based board dependency minimum. To make these edits in the map editor instead, with Save writing the document and the dependency, see [edit and save fixed placement in the map editor](editing-placement-in-the-editor.md). At runtime the patcher scans loaded packs in pack order. In the map editor it reads only the active edited MapMod's document.

Save the document as strict UTF-8 JSON. Use the `mapId` of the selected Map definition or one of its resolved map layers. Keep one document with the supplying MapMod so editor inspection and runtime application read the same desired state.

## Describe a fixed placement

In the map editor, open **Scene patches**, refresh the inventory, find the board, and select its inventory row labeled `Inspect <family>: <path>` to display its target identity and dependencies. Select **Copy inspected identity** to copy that JSON. **Focus inspected object** moves the editor camera without changing the inspected selection. Refresh only inspects; it does not apply patches. Add a unique modification ID and dependencies to the copied identity fields. See [scene inspection](inspect-scene-objects.md) for stale observations, dependency limits and the wider inspection-only inventory.

Add one entry to `modifications` for each desired placement. The following example shows the v1 field shape and the supported target identity. Replace the sample map ID and coordinates with values for the selected map.

```json
{
  "formatVersion": 1,
  "sourceMap": {
    "mapId": "example-map-id"
  },
  "modifications": [
    {
      "id": "move-example-object",
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
  ]
}
```

The target must resolve to exactly one board in the loaded map context that matches the board pattern. The position has three finite numbers in map coordinates. The rotation has four finite numbers in quaternion order and must be a unit quaternion. The adapter converts map coordinates through the game's world transformer. It updates the culling position and rebuilds the circle map mask after moving the board.

Use a unique, non-empty `id` for each modification. List prerequisite modification IDs in `dependsOn`; leave it empty when there are no prerequisites. A missing, failed, or cyclic dependency prevents the dependent modification from applying.

## Check the support boundary

The adapter requires the board's parents, culler, and circle map mask to be active; the board itself may be disabled through `active`. It rejects a height-flattening mask, `Track.*` components and other culling or mask components in the hierarchy, and checks the required map conversion, culling, mask, and terrain hooks. For the supported bridge, see [move or disable a bridge with its collision](placing-bridges.md); other object families are not implemented. The runtime reports `Capability.Unchecked` even when these prerequisites are available. The isolated fixture observations described above provide evidence for the tested board and build; they do not change that capability classification or establish the complete culling range.

If a modification fails during a load, the engine does not retry it during that load. Correct the document or target before a later load.

If you correct a document outside the editor, reload the map before retrying. For the exact v1 field requirements, see [scene-patch document v1](scene-patch-document-v1.md).
