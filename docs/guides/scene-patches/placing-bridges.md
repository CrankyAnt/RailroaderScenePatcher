<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a bridge with its collision

This guide shows how to move, rotate, disable and restore a supported bridge, and what stays where it is. Bridge placement moves the bridge's own hierarchy, including its colliders and culling. It does not move track, generated trestles or the construction scenery beside the bridge, and it does not change which track the bridge belongs to.

The supported bridge is the Ela bridge of the Bushnell-Whittier map layer, at `World/Large Scenery/Ela/Ela Bridge` with a `Helpers.Culling.RendererCuller` component. Other bridges show up in the inventory for inspection only. A bridge target requires patcher 0.5.0 or later. The editor's Save derives the dependency minimum from the document's content; see [the document reference](scene-patch-document-v1.md#dependency-minimum).

## Move the bridge in the editor

1. Open **Scene patches**, select **Refresh** in the command band, search for `Ela Bridge` and select its row in the outliner. **Select in world** with a click on the bridge's marking also selects it, as described in [select world targets](select-world-targets.md). The **Identity** section of the **Object** tab shows the structure `bridge-colliders-v1`. If **Detected problems** on the **Object** tab lists **Bridge placement unavailable**, the bridge does not match the supported pattern and the reason follows.
2. Select **Focus** in the command band to see the bridge and the track beside it.
3. On the **Placement** tab, in **Numeric** mode, change the six fields: position in map coordinates and rotation in degrees. Select **Apply** in the footer.

The bridge root moves at once, with every collider below it. Save, Undo, Redo and **Remove** on the **Changes** tab work as described in [edit and save fixed placement in the map editor](editing-placement-in-the-editor.md). Removal returns the bridge to the pose the loaded map gives it.

Moving the bridge away from its track leaves the track and the sampled nearby trestles where they were. How trains behave on a moved or disabled bridge has not been tested. Check the result in the world: the patcher does not align the bridge with the track for you.

## Disable or enable the bridge

Select **Disable** in the footer of the **Object** tab. The bridge disappears and its colliders stop colliding. The colliders keep their own enabled settings, so **Enable**, Undo or removal brings back the same collision. In the inspected build, the construction-scenery controller references the bridge's abutments and steel below the root, not the root itself, and the patcher refuses a bridge whose root is a construction target. A later construction-state transition has not been tested, so it is unverified whether an explicit disable holds through one.

## Write the modification by hand

Copy the identity with **Copy ID** in the command band and add it to `Data/scene-patches.json` as described in [scene-patch document v1](scene-patch-document-v1.md):

```json
{
  "id": "place-ela-bridge",
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

After each change it checks that the root has the authored pose and activity, that every collider kept its identity, shape and position relative to the root, that collider activity follows the root, and that the nearby generated trestles and the construction-scenery controller did not change. Any difference fails the modification and restores the bridge. If the restoration cannot be verified, the patcher reports incomplete cleanup and refuses further edits of the bridge until **Recheck** succeeds or the world reloads.

## Limits

The automated tests check this behavior with a controlled bridge. The trestle comparison covers loaded trestles within 250 m of the bridge's first observed position. Bounds of mesh colliders are not compared. An isolated native test on Railroader 0.6.2 build 26335745 with patcher source `afe9d1b` observed the Ela bridge's seven box colliders keep their shapes and follow the root through move, disable, native Undo and Redo, removal, Save and an editor reload, without duplicated colliders after the reload. A disposable test ball recorded physics contact with a bridge collider at the original, moved, restored and reloaded poses. The two sampled nearby trestles and the sampled construction-scenery relationships stayed unchanged. That test covers this bridge and build only, not other bridges or builds, ordinary-play loading or an origin shift during edits. Collision under trains, construction-state transitions and multiplayer are not covered.
