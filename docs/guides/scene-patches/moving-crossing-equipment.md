<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable an existing crossing mast or bell

Use the patcher to move, rotate, disable, or enable a mast of an existing crossing, or to move, disable, or enable its bell. The equipment continues to respond to its crossing controller. You can also move or rotate the whole grade-crossing setup through its crossing controller. This guide also explains how culling works after you move a mast.

To add equipment, see [add lights and gates to an existing crossing](adding-crossing-equipment.md) and [add a bell to an existing crossing](adding-crossing-bells.md). For the native map editor workflow, including Save, Undo, and Redo, see [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). For the JSON fields, see [scene patch file v1](scene-patch-file-v1.md#existing-crossing-mast-and-bell).

## Find the mast or bell

In the native map editor, open **Scene Patcher** and click **Refresh** in the command band.

- Each crossing mast has one row under **Grade crossing**, named for the mast and its crossing, such as `Signal 1 at Bryson Everett`. The row represents the mast's light object, for example `Grade Crossing - Bryson Everett/Signal 1/Signal 1`, and includes the gate on a child of that object. The gate has no separate row. The **Connections** tab lists each light and gate, the key each observes, and the crossing controller that drives them.
- The bell that a crossing controller creates has one row under **Grade crossing**, such as `Crossing Bell at Bryson Everett`.
- Each detection span appears under its controller in the outliner. The **Object** tab starts with **Detection span of:** and the controller's name. You cannot set a span's placement because its track locations determine its position. To change which tracks the crossing detects, select the span and click **Select controller** on the **Object** tab. Then use the controller's **Connections** tab, as described in [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md).

**Supported edits** on the **Object** tab lists the edits you can make under **Available:** and the edits you cannot make under **Unavailable:**, each with its reason. A mast or bell that does not match its supported source pattern has its edits under **Unavailable:**, with the reason. See [refusal reasons](#refusal-reasons).

## Move or rotate a mast, or move a bell

1. Select the mast or bell in the outliner, or click **Select in world** and select it in the world.
2. On the **Placement** tab, choose a mode:
   - **Numeric**: enter the position in map coordinates. For a mast, you can also enter the rotation in degrees. Click **Apply**.
   - **Gizmo**: drag the native map editor's Move gizmo in the world or, for a mast, the Rotate gizmo.
   - **Snap**: move the mast or bell onto a selected track or endpoint in a single edit, as described in [snap an object to a track](snapping-to-track.md).
3. Check the result in the world. The tab's fields show the physical placement set by the scene patch.

The mast's light and gate move and rotate together. The crossing controller, the other mast, and the bell stay where they are. The mast still observes its crossing controller, which continues to switch its lights and lower its gate.

Existing crossing equipment uses fixed placement. It cannot use track-following placement, so **Follow** is unavailable.

A bell's sound has no direction, so rotation has no effect. The **Rotation** values remain visible as text, and the gizmo's **Rotate** tool is disabled. Both explain why rotation isn't editable. **Snap** sets the bell's position and writes a rotation, but that rotation has no effect.

## Move or rotate the whole grade-crossing setup

To move a crossing's masts and bell together, move the setup through its crossing controller.

1. Select the crossing controller in the outliner, or click **Select in world** and select it in the world.
2. On the **Placement** tab, choose **Numeric**, **Gizmo**, or **Snap**, as for a mast. **Follow** is unavailable because a grade-crossing setup uses fixed placement.
3. Check the result in the world.

The masts, with their lights and gates, and the bell move and rotate together. They keep their placement relative to each other and to the controller, including a mast or bell that you moved on its own. These objects stay where they are:

- The crossing pads and the whistle signs. Their track bindings set their places. To move a pad, see [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md).
- The detection spans. The crossing still detects trains on the same tracks. To change them, see [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md).
- Lights, gates, and bells that a mod added to the crossing. Each stays at the placement that its addition records. To move one, edit it on the controller's **Connections** tab.

The road and the track are native map data, so they don't move either.

A duplicated crossing moves and rotates the same way. Its pads stay where they are, so rebind them if the track at the new location differs.

The crossing's culler moves with the setup. A mast that you move outside the culler's sphere on its own gets its own culling sphere, as described in the next section.

A crossing controller can't be disabled, so **Disable** stays unavailable on its **Object** tab.

### Culling of a moved mast

The game hides a crossing's masts when the camera is far from the crossing. If you move a mast outside the crossing's culling sphere, the patcher gives the mast its own sphere. This keeps it visible when the camera sees it at its new position. The crossing's culler keeps its radius and continues to hide and show the other mast. Moving the mast back, undoing the move, or removing the modification returns the mast to the crossing's culler.

**Runtime state** on the **Object** tab shows the culling state recorded by the last **Refresh** or edit. It names the object in the crossing's culler list and shows whether that object was removed from the list so the mast could use its own culling sphere. It gives each separate sphere's center and radius.

The section also lists every reference set in the scene that points to an object above the mast, including references in a map feature's or progression's switch list. Switching any of those objects also switches the mast.

## Disable or enable a mast or bell

On the **Object** tab, click **Disable** or **Enable**. Each Enabled change is a separate operation with its own Undo step. It doesn't change physical placement, and a placement edit doesn't change Enabled.

- Disabling a mast switches off its light object, including the lights and gate. The crossing controller keeps running for the rest of the crossing.
- Disabling the bell silences it. The game's bell player discards its sound when disabled, so the patcher configures it with the crossing's bell sound when you enable it again.

The patcher applies an explicit `"active": false` in the scene patch file on every load. The mast stays disabled as the game's culler hides and shows the crossing. Without that value, the mast follows its crossing's culler as before.

**Clear activity** removes this mod's Enabled value. The mast or bell returns to the activity of its effective underlying state.

## Undo, Redo, removal and reload

Native **Undo** and **Redo** reverse and reapply each completed move, rotation, and Enabled change, including changes to the mast's culling. The **Saved** or **Unsaved** status lamp and the native map editor's **Save** button reflect any outstanding changes.

Click **Remove modification** in the footer to remove this mod's physical placement and Enabled value for the object. The patcher recalculates the mast or bell's placement relative to its parent, using the scene and any remaining mod values. It doesn't restore an earlier snapshot.

Clicking the native **Save** button writes the modification to the edited mod's scene patch file. When the world reloads, the patcher reapplies the file's placement and activity. Applying the same file again makes no further change.

## What the file records

A modification of a mast or bell records a fixed placement, an Enabled value, or both. It records no controller, key, track, or circuit. The equipment's logical binding to its crossing stays unchanged and is not part of the modification. Its `placement` is recorded relative to the crossing controller, as described under [the existing crossing mast and bell target](scene-patch-file-v1.md#existing-crossing-mast-and-bell).

A whole move records a fixed placement in the crossing controller's modification, next to any crossing coverage and crossing equipment that the modification holds. See [the crossing target](scene-patch-file-v1.md#crossing).

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

The patcher edits only masts and bells that match their supported source pattern. Other equipment stays in the outliner for inspection. All its placement modes, including **Numeric**, are disabled. Its position and rotation fields are disabled too, as are the **Enable** and **Disable** buttons. **Numeric** still shows physical placement as text. The tooltips show `Unavailable: <reason>`, using the reason in **Supported edits**.

Click **Why this equipment cannot be edited** on the **Object** tab to open this section. A modification targeting unsupported equipment fails before anything changes. The other modifications in the scene patch file still apply.

A mast matches `existing-crossing-mast-v1` when it meets every requirement below:

| Requirement | Reason shown when it fails |
| --- | --- |
| The object has a crossing lamp component, `Track.KeyValueLampMaterialController`. A gate without a light object remains listed for inspection but cannot be edited. | this gate does not stand on a light object |
| The nearest key-value object above it belongs to a crossing controller. | no crossing controller drives it |
| Each lamp and each gate observes a key that the crossing controller publishes: `lights` or `gates`. | lamp or gate ... observes key ..., which the crossing controller does not publish |
| Every lamp target is inside the mast's hierarchy. | lamp ... targets an object outside the mast |
| No gate is elsewhere under the light object's parent. Such a gate would stay behind when the light object moves. | a gate stands beside this light object |
| The mast contains no second light object, crossing controller, key-value object, detection span, game-object culler, bell player, or rigidbody. | it holds ... |
| No script in its hierarchy is missing. | a script in its hierarchy is missing |
| No track-relative component switches it with a track group. | a track-relative component switches it with a track group |
| No other loaded object shares its hierarchy path. | ... objects share this hierarchy path |
| The patcher did not add it. Edit an added light or gate on its crossing controller's **Connections** tab. | the patcher added this equipment |

A bell matches `existing-crossing-bell-v1` when it is the bell player created by its crossing controller, the controller is enabled, no script is missing, and no other loaded object shares its path.

An operation can be unavailable even when other edits are supported. Its control stays visible but disabled, with the tooltip `Unavailable: <reason>`:

- **Enable** and **Disable** are unavailable when a culler of the game itself enables and disables the equipment by camera distance. That culler would override a saved Enabled value. This was checked at one base-game map crossing in Railroader 0.6.3, Bryson Everett. Its culler lists each mast's parent, so Enabled is supported for those masts. **Runtime state** on the **Object** tab shows which object the culler lists for the inspected mast.
- **Enable** and **Disable** are unavailable for a bell if the game build lacks what the patcher needs to configure the bell again after enabling it, such as unavailable crossing-equipment prerequisites. The tooltip names what the build lacks.
- **Disable** is unavailable for a mast or bell that a progression controller or map feature switches when the disable could not hold, because the patcher cannot share its activity in this load or the game's members of that switch are unavailable. The tooltip gives that reason. A scene patch file that disables such a mast or bell fails that modification before changing anything, and the file's other modifications still apply. No base-game crossing has such a mast or bell in Railroader 0.6.3.
- **Follow** is unavailable for every existing mast and bell. A modification that gives one a `trackFollowing` placement fails before changing anything.
- Rotation is unavailable for a bell.

## Multiplayer, later joins and reconnects

Each player's game moves or disables the mast or bell in its own world when that world loads. When a player joins later or reconnects, their game loads the map again. The patcher applies each modification once to the equipment in the newly loaded world. A moved mast then has its culling set up again. If a modification fails in one player's game, that game reports it. The failure doesn't affect the other players. See [application on each peer](scene-patch-file-v1.md#application-on-each-peer).

These statements come from the patcher's automated tests and from the code of Railroader 0.6.3. They have not been tested in a multiplayer session.

## Limits

- A crossing controller creates its bell each time the controller is enabled. If the game switches the controller off and on during a session, the new bell appears at the controller and stays enabled until the next world load reapplies the scene patch file.
- The patcher does not move the crossing controller, the road, the crossing pads, or the detection spans with a mast.
- A whole move leaves each crossing pad object where it is. When the world reloads, the patcher builds a duplicated crossing at its saved placement, so the pad objects of the duplicate stand around that placement again. A bound pad's surface stays on its track either way, because the game generates it along the track binding.
- The patcher sizes a moved mast's culling sphere around the mast and the gate arm's position when it plans the move. The arm can extend beyond that sphere when it swings later.
- No mast or bell in the Railroader 0.6.3 base-game map follows a track group. Equipment that does is outside the mast's supported source pattern.
