<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable an existing crossing mast or bell

You can move, rotate, disable or enable a mast of an existing crossing. You can also move, disable or enable its bell. This guide explains those edits, how the game hides a moved mast at a distance, and how the equipment keeps responding to its crossing controller.

To add equipment, see [add lights and gates to an existing crossing](adding-crossing-equipment.md) and [add a bell to an existing crossing](adding-crossing-bells.md). For the native map editor workflow, including Save, Undo and Redo, see [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). For the JSON fields, see [scene patch file v1](scene-patch-file-v1.md#existing-crossing-mast-and-bell).

## Find the mast or bell

In the native map editor, open **Scene patches** and click **Refresh** in the command band.

- Each crossing mast has one row in the **Crossing mast** family, named for the mast and its crossing, such as `Signal 1 at Bryson Everett`. The row represents the mast's light object, for example `Grade Crossing - Bryson Everett/Signal 1/Signal 1`. Its gate is on a child of the light object and is part of the same row; the gate has no row of its own. The **Connections** tab lists each light and gate, the key each observes, and the crossing controller that drives them.
- The bell that a crossing controller creates has one row in the **Crossing bell** family, such as `Crossing Bell at Bryson Everett`.
- Each detection span appears under its controller in the **Crossing controller** family. The **Object** tab starts with **Detection span of:** and the controller's name. You cannot set a span's placement because its track locations determine its position. To change which tracks the crossing detects, select the span and click **Select controller** on the **Object** tab. Then use the controller's **Connections** tab, as described in [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md).

**Supported edits** on the **Object** tab lists what you can change. If it starts with **Not editable:**, the mast or bell does not match its supported source pattern. The text explains why. See [refusal reasons](#refusal-reasons).

## Move or rotate a mast, or move a bell

1. Select the mast or bell in the outliner, or click **Select in world** and select it in the world.
2. On the **Placement** tab, choose a mode:
   - **Numeric**: enter the position in map coordinates. For a mast, you can also enter the rotation in degrees. Click **Apply**.
   - **Gizmo**: drag the native Move gizmo in the world or, for a mast, the Rotate gizmo.
   - **Snap**: move the mast or bell onto a selected track or endpoint in a single edit, as described in [snap an object to a track](snapping-to-track.md).
3. Check the result in the world. The tab's fields show the physical placement set by the scene patch.

The mast's light and gate move and rotate together. The crossing controller, the other mast and the bell stay where they are. The mast still observes its crossing controller, which continues to switch its lights and lower its gate.

Existing crossing equipment uses fixed placement. It cannot use track-following placement, so **Follow** is unavailable.

A bell's sound has no direction, so rotation does not apply to a bell. The **Rotation** values remain visible without editable fields, and the gizmo's **Rotate** tool is disabled. Both explain why rotation is unavailable. **Snap** sets the bell's position and writes a rotation, but that rotation has no effect.

### Culling of a moved mast

The game hides a crossing's masts when the camera is far from the crossing. If you move a mast outside the crossing's culling sphere, the patcher gives the mast its own sphere. This keeps it visible when the camera sees it at its new position. The crossing's culler keeps its radius and continues to hide and show the other mast. Moving the mast back, undoing the move or removing the modification returns the mast to the crossing's culler.

**Runtime state** on the **Object** tab shows the culling state from the last **Refresh** or edit. It names the object in the crossing's culler list and shows whether that object left the list so the mast could use its own culling sphere. It also shows the center and radius of each separate sphere. It also lists every reference set in the scene to an object above the mast, including references in a map feature's or progression's switch list. Switching one of those objects also switches the mast.

## Disable or enable a mast or bell

On the **Object** tab, click **Disable** or **Enable**. Enabled is its own operation, with its own Undo step. Changing Enabled leaves the physical placement unchanged. Changing the physical placement leaves Enabled unchanged.

- Disabling a mast switches off its light object, including the lights and gate. The crossing controller keeps running for the rest of the crossing.
- Disabling the bell silences it. The game's bell player discards its sound when disabled, so the patcher configures it with the crossing's bell sound when you enable it again.

The patcher applies an explicit `"active": false` in the scene patch file on every load. The mast stays disabled as the game's culler hides and shows the crossing. Without that value, the mast follows its crossing's culler as before.

**Clear activity** removes this mod's Enabled value. The mast or bell returns to the activity of its effective underlying state.

## Undo, Redo, removal and reload

Native **Undo** and **Redo** reverse and reapply each completed move, rotation and Enabled change, including changes to the mast's culling. The **Saved** or **Unsaved** status lamp and the native map editor's **Save** button reflect any outstanding changes.

Click **Remove modification** in the footer to remove this mod's physical placement and Enabled value for the object. The patcher recalculates the mast or bell's placement relative to its parent from the scene and any remaining mod values. It does not restore an earlier snapshot.

Clicking the native **Save** button writes the modification to the edited mod's scene patch file. When the world reloads, the patcher reapplies the file's placement and activity. Applying the same file again makes no further change.

## What the file records

A modification of a mast or bell records a fixed placement, an Enabled value, or both. It records no controller, key, track or circuit. The equipment's logical binding to its crossing stays unchanged and is not part of the modification.

```json
"move-signal-1": {
  "target": {
    "scene": "BushnellWhittier-MapManager",
    "path": "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett/Signal 1/Signal 1",
    "component": "Track.KeyValueLampMaterialController",
    "structure": "existing-crossing-mast-v1"
  },
  "placement": { "mode": "fixed", "position": [4380.5, 529.1, 5391.0], "rotation": [0, 0.7071068, 0, 0.7071068] },
  "active": true,
  "dependsOn": []
}
```

## Refusal reasons

The patcher edits only masts and bells that match their supported source pattern. Equipment that does not match stays in the outliner. **Numeric** still shows its physical placement, but the other placement modes and the **Enable** and **Disable** buttons are disabled. Their tooltips show `Unavailable: <reason>`, using the reason in **Supported edits**. A modification that names such equipment fails before changing anything. The other modifications in the scene patch file still apply.

A mast matches `existing-crossing-mast-v1` when it meets every requirement below:

| Requirement | Reason shown when it fails |
| --- | --- |
| The object has a crossing lamp component, `Track.KeyValueLampMaterialController`. A gate without a light object remains listed for inspection but cannot be edited. | this gate does not stand on a light object |
| The nearest key-value object above it belongs to a crossing controller. | no crossing controller drives it |
| Each lamp and each gate observes a key that the crossing controller publishes: `lights` or `gates`. | lamp or gate ... observes key ..., which the crossing controller does not publish |
| Every lamp target is inside the mast's hierarchy. | lamp ... targets an object outside the mast |
| No gate is elsewhere under the light object's parent. Such a gate would stay behind when the light object moves. | a gate stands beside this light object |
| The mast contains no second light object, crossing controller, key-value object, detection span, game-object culler, bell player or rigidbody. | it holds ... |
| No script in its hierarchy is missing. | a script in its hierarchy is missing |
| No track-relative component switches it with a track group. | a track-relative component switches it with a track group |
| No other loaded object shares its hierarchy path. | ... objects share this hierarchy path |
| The patcher did not add it. Edit an added light or gate on its crossing controller's **Connections** tab. | the patcher added this equipment |

A bell matches `existing-crossing-bell-v1` when it is the bell player created by its crossing controller, the controller is enabled, no script is missing and no other loaded object shares its path.

An operation can be unavailable even when other edits are supported. Its control stays visible but disabled, with the tooltip `Unavailable: <reason>`:

- **Enable** and **Disable** are unavailable when a culler of the game itself enables and disables the equipment by camera distance. That culler would override a saved Enabled value. This was checked at one base-game map crossing in Railroader 0.6.3, Bryson Everett. Its culler lists each mast's parent, so Enabled is supported for those masts. **Runtime state** on the **Object** tab shows which object the culler lists for the inspected mast.
- **Enable** and **Disable** are unavailable for a bell if the game build lacks what the patcher needs to configure the bell again after enabling it, such as unavailable crossing-equipment prerequisites. The tooltip names what the build lacks.
- **Follow** is unavailable for every existing mast and bell. A modification that gives one a `trackFollowing` placement fails before changing anything.
- Rotation is unavailable for a bell.

## Multiplayer, later joins and reconnects

Each player's game moves or disables the mast or bell in its own world when that world loads. A player who joins later or reconnects loads the map again, and the patcher applies each modification once to the equipment of the newly loaded world. A moved mast then gets its culling again. If a modification fails in one player's game, that game reports it, and the other players are not affected. See [application on each peer](scene-patch-file-v1.md#application-on-each-peer).

These statements come from the patcher's automated tests and from the code of Railroader 0.6.3. They have not been tested in a multiplayer session.

## Limits

- A crossing controller creates its bell each time the controller is enabled. If the game switches the controller off and on during a session, the new bell appears at the controller and stays enabled until the next world load reapplies the scene patch file.
- The patcher does not move the crossing controller, the road, the crossing pads or the detection spans with a mast.
- The patcher sizes a moved mast's culling sphere around the mast and the gate arm's position when it plans the move. The arm can extend beyond that sphere when it swings later.
- No mast or bell in the Railroader 0.6.3 base-game map follows a track group. Equipment that does is outside the mast's supported source pattern.
