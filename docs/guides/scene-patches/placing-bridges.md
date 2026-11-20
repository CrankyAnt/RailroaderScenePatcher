<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a bridge with its collision

Move, rotate, disable or restore a supported bridge with its colliders and culling. Everything in the bridge's own hierarchy moves with it. Track, generated trestles and construction scenery beside the bridge stay where they are. The bridge still belongs to the same track.

The supported bridge is the Ela bridge of the Bushnell-Whittier map layer, at `World/Large Scenery/Ela/Ela Bridge` with a `Helpers.Culling.RendererCuller` component. Rail bridges outside Ela, such as Bryson Bridge 1, the Almond Bridge and the plate girder bridges, are baked scenery groups; see [rail bridges outside Ela](moving-baked-scenery-groups.md#rail-bridges-outside-ela). Road bridges are baked scenery groups too; see [road bridges](moving-baked-scenery-groups.md#road-bridges). A bridge target requires patcher 0.5.0 or later. The native map editor's Save derives the dependency minimum from the scene patch file's content; see [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## Move the bridge in the native map editor

1. Open **Scene patches**, click **Refresh** in the command band, search for `Ela Bridge` and select its row in the outliner. You can also click **Select in world** and select the bridge by its marking, as described in [select world targets](select-world-targets.md). The **Identity** section of the **Object** tab shows the structure `bridge-colliders-v1`. If **Supported edits** on the **Object** tab starts with **Not editable:**, the bridge doesn't match the supported pattern. The text explains why.
2. Click **Focus** in the command band to see the bridge and the track beside it.
3. On the **Placement** tab, choose **Numeric** mode and change the six fields for position in map coordinates and rotation in degrees. Click **Apply** at the bottom of the tab.

The bridge root moves at once, with every collider below it. Save, Undo, Redo and **Remove modification** in the footer work as described in [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). Removal returns the bridge to the placement the loaded map gives it.

Moving the bridge away from its track leaves the track and the sampled nearby trestles where they were. Train behavior on a moved or disabled bridge hasn't been tested. Check the result in the world. The patcher doesn't align the bridge with the track for you.

## Disable or enable the bridge

Click **Disable** at the bottom of the **Object** tab. The bridge disappears and its colliders stop colliding. Each collider keeps its own enabled setting, so **Enable**, Undo or removal restores the same collision. In the inspected build, the construction-scenery controller references the bridge's abutments and steel below the root. It doesn't reference the root itself. The patcher refuses a bridge whose root is a construction target. A later construction-state transition hasn't been tested, so it's unverified whether an explicit disable holds through one.

## Write the modification by hand

Copy the identity with **Copy ID** in the command band and add a modification to the `modifications` object of `Data/scene-patches.json`, under a key of your choice, as described in [scene patch file v1](scene-patch-file-v1.md):

```json
"place-ela-bridge": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Ela/Ela Bridge",
    "component": "Helpers.Culling.RendererCuller",
    "structure": "bridge-colliders-v1"
  },
  "placement": {
    "mode": "fixed",
    "position": [0.0, 0.0, 0.0],
    "rotation": [0.0, 0.0, 0.0, 1.0]
  },
  "dependsOn": []
}
```

Use the scene name and coordinates from the inspected identity. Add `"active": false` to disable the bridge.

## What the patcher checks

Before it touches the bridge, the patcher requires:

- active parents and an enabled renderer culler on the bridge root; the root itself may be disabled by your modification,
- at least one collider in the bridge hierarchy and no rigidbody,
- no track-owned component in the bridge hierarchy, such as `Track.*` or a generated `AutoTrestle`,
- no construction-scenery entry that switches the bridge root,
- the same colliders, shapes and positions relative to the root that the bridge had when the patcher first saw it in this load.

After each change, the patcher checks the root's placement and activity against the mod values. Every collider must keep its identity, shape and position relative to the root, and collider activity must follow the root. The nearby generated trestles and the construction-scenery controller must stay unchanged. If any check finds a difference, the modification fails and the patcher restores the bridge. If it can't verify restoration, it reports incomplete cleanup and refuses further edits to the bridge until **Recheck** succeeds or the world reloads.

## Limits

The automated tests check this behavior with a controlled bridge. The trestle comparison covers loaded trestles within 250 m of the bridge's first observed position. Bounds of mesh colliders are not compared. An isolated native test on Railroader 0.6.2 build 26335745 with patcher source `afe9d1b` observed the Ela bridge's seven box colliders keep their shapes and follow the root through move, disable, native Undo and Redo, removal, Save and a native map editor reload, without duplicated colliders after the reload. A disposable test ball recorded physics contact with a bridge collider at the original, moved, restored and reloaded placements. The two sampled nearby trestles and the sampled construction-scenery relationships stayed unchanged. That test covers this bridge and build only, not other bridges or builds, ordinary-play loading or an origin shift during edits. Collision under trains, construction-state transitions and multiplayer are not covered.
