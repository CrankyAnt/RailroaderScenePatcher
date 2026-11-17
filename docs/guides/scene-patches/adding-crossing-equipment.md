<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Add lights and gates to an existing crossing

This guide shows how to add crossing lights and gates to an existing grade crossing, in the native map editor or in `Data/scene-patches.json`. The patcher copies a light or gate device that is already loaded in your map and lets the crossing controller drive the copy. It does not create new crossings, edit models or add bells; bells follow a separate pattern.

## Which devices qualify

A device is eligible when it works through the crossing controller's keys. The patcher calls this the `crossing-light-gate-v1` pattern:

- Each lamp is a `Track.KeyValueLampMaterialController` that observes the controller's `lights` or `gates` key. Its lamp targets lie inside the device, and at least one renderer under them uses its lamp material.
- Each gate is a `RollingStock.Controls.KeyValueBoolAnimator` that observes the `lights` or `gates` key. It has an `Animator` on its own object and an animation clip.
- The device holds at least one such lamp or gate.
- The device holds no crossing controller, key-value object, detection span, culler or bell player, and no missing script.
- The device lies in the same scene as the destination crossing, so its assets stay loaded as long as the copy exists.
- The destination crossing controller has exactly one key-value object, on its own object, and at most one culler.

The model, materials, colours and animation do not matter. A device the patcher has never seen before qualifies when it works through these mechanisms; a familiar-looking device that does not is rejected. Similar names or appearance do not qualify a device.

The source must already be loaded in the map you are editing. The patcher never loads another scene to find a source, and it never substitutes another device when the named source is missing or ambiguous.

### Rejected sources

Scene patch editing lists every device beneath a loaded crossing controller, including the bell player each controller generates, and gives each rejected one its concrete reason:

| Reason | Meaning |
| --- | --- |
| holds a bell player | Bells use the controller-bound bell pattern, not this one. |
| holds its own key-value object | The copy's lamps and gates would observe that object instead of the crossing controller. |
| holds a crossing controller, detection span or culler | A copy would duplicate a crossing, detection or culling. |
| observes key `...`, which the crossing controller does not publish | The lamp or gate would never respond. |
| targets an object outside the source | A copy would drive the original device. |
| has no renderer under its targets that uses its lamp material | The lamp would show no change. |
| has no Animator or no animation clip | The gate would not move. |
| holds no lamp and no gate that observes the controller's lights or gates key | It is not crossing equipment. |
| lies in another scene than the destination crossing | That scene could unload the shared assets while the copy still uses them. |
| the destination crossing controller has no or several key-value objects, or several cullers | The copy could not be addressed or culled unambiguously. |
| is equipment this patcher added | Copy the map's own device instead. |
| a script in its hierarchy is missing | The device cannot be checked or copied safely. |

## What an addition does

- The copy is placed beneath the crossing controller. Its lamps and gates find the controller's key-value object and follow the same lights and gates state as the native equipment, from the same circuits.
- The copy joins the controller's culler and takes its current culling state, so it is shown and hidden with the crossing's own equipment. A culled copy initializes when it becomes visible.
- The copy shares the source's model, materials and animation. They belong to the source's scene, which is the crossing's scene, so they stay loaded while the copy exists. The copy does not depend on the source object itself: it keeps working when the source is culled or removed from view. The patcher owns only the copy's objects; removing the copy destroys those objects and never the shared assets, so the source keeps working.
- Each addition is an added object: the outliner lists it once in the family **Added light or gate**, marked with `+`, and its state line shows **+ ADDED**. The parts of the copy are not listed, and its culling, initialization and Undo/Redo steps do not mark the inventory stale. You edit it on its crossing's **Connections** tab.
- An addition that fails its check blocks only equipment edits of that crossing until the world reloads. Coverage and detection edits of the same crossing stay available.
- The crossing object, its coverage and its native equipment do not change.
- Each addition has an ID that is unique in your scene patch file: one modification may use it, and two modifications that share it both fail. Scene patch editing refuses an ID another modification already uses. The patcher scopes it to your mod, so another supplying mod can use the same ID without a conflict, and a scene patch file never moves or removes another mod's additions.

## Add equipment in the native map editor

1. Open the Scene patches window and inspect the crossing controller, from the outliner (family **Crossing controller**) or by selecting it in the world.
2. On the **Connections** tab, read **Lights and gates** for the current additions and **Add a light or gate** for the devices. Each rejected device shows its reason; **Why a device is rejected** opens the table above.
3. Select **Use** for an eligible device; each button names the device and its crossing, such as **Use Signal 1 at Allen**. The form **Light or gate** opens at the top of the tab. Its proposed **Equipment ID** is not yet used in your scene patch file, with a numeric suffix when the name is taken. Its proposed placement puts the copy at the inspected crossing as the device stands at its own crossing, five metres aside. Change the **Equipment ID**, the **Position** in map coordinates and the **Rotation** in degrees as needed.
4. Select **Preview** at the bottom of the tab. The **Preview** section shows what will be added, moved, rebuilt or removed. Nothing changes yet.
5. Select **Apply** at the bottom of the tab. Apply uses the previewed change; preview again after you change a field.

To move an addition, select **Edit** with its ID, change the fields, preview and apply. To remove one, select **Edit**, then **Remove** at the bottom of the tab and apply. **Remove modification** in the footer removes your mod's coverage, detection additions and lights and gates of the crossing at once. Native Undo and Redo reverse and repeat each applied change, including rebuilding a removed addition.

**Lights and gates** shows each addition's state: visible with its lamp and gate observers registered, initialization pending, or culled with its crossing. An addition of your scene patch file that could not be built, for example because its source is missing, is listed with **Not built** and its reason; **Edit** opens it so you can correct it or remove it.

Save writes the additions to `Data/scene-patches.json` and raises the patcher dependency in `Info.json` to `>=0.5.0`. The game does not save the copies in native map data; the patcher rebuilds them from the scene patch file on every load, and a repeated load reconciles existing copies instead of duplicating them.

If an edit fails, the message area reports whether the prior equipment was restored. When cleanup is incomplete, the crossing refuses further edits until **Recheck** in the footer succeeds or the native map editor reloads.

## Write equipment in the scene patch file

Target the crossing controller as for coverage, with `component` `Track.Crossings.GradeCrossingController` and `structure` `crossing-coverage-v1`, and key the additions by their IDs under `relationships.crossingEquipment`. The modification is itself an entry of `modifications`, keyed by its ID:

```json
"equip-bryson-everett": {
  "target": {
    "scene": "BushnellWhittier",
    "path": "World/Crossings/Bryson Everett",
    "component": "Track.Crossings.GradeCrossingController",
    "structure": "crossing-coverage-v1"
  },
  "relationships": {
    "crossingEquipment": {
      "east-signal": {
        "pattern": "crossing-light-gate-v1",
        "source": { "scene": "BushnellWhittier", "path": "World/Crossings/Bryson Everett/Signal 1" },
        "placement": { "mode": "fixed", "position": [4630.5, 529.2, 5462.1], "rotation": [0, 0.7071068, 0, 0.7071068] }
      }
    }
  },
  "dependsOn": []
}
```

The scene, paths and coordinates are placeholders; use the values scene patch editing shows for your crossing and source. The modification that targets a crossing records the controller relationship: its additions belong to that controller. An entry holds no `id` member: its key is its ID. The field rules are in the [scene patch file v1 reference](scene-patch-file-v1.md#crossing-equipment).

## Limits

- Additions have a fixed map placement. Track-following placement, the transform gizmo and snapping are not available for additions.
- Placement, rotation and activity of the crossing's native equipment are not edited here.
- Copies have the colliders of their source. The patcher adds no collision handling.
- The pattern and its checks were derived from the game build Railroader 0.6.2 build 26335745 and from one demonstrated combined light/gate device. Native behavior in other builds and for other devices follows from the checked mechanisms, not from a trial of each device.
