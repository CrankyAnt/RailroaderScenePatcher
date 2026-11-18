<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Add lights and gates to an existing crossing

You can add lights and gates to an existing grade crossing in the native map editor or in `Data/scene-patches.json`. The patcher copies a light or gate device already loaded in your map. The crossing controller then drives the copy.

This does not create a crossing or edit a model. Bells use a different pattern, described in [add a bell to an existing crossing](adding-crossing-bells.md).

## Which devices qualify

A device qualifies if it responds to the crossing controller's keys and meets the requirements below. This is the `crossing-light-gate-v1` pattern:

- Each lamp is a `Track.KeyValueLampMaterialController` that observes the controller's `lights` or `gates` key. Its lamp targets are inside the device. At least one renderer under those targets uses the lamp's material.
- Each gate is a `RollingStock.Controls.KeyValueBoolAnimator` that observes the `lights` or `gates` key. It has an `Animator` on its own object and an animation clip.
- The device contains at least one such lamp or gate.
- The device contains no crossing controller, key-value object, detection span, culler or bell player. It has no missing script.
- The device is in the same scene as the destination crossing, so its assets stay loaded as long as the copy exists.
- The destination crossing controller has exactly one key-value object, on the controller's own object, and at most one culler.

Within these requirements, the model, materials, colours and animation do not matter. An unfamiliar device qualifies if it uses these mechanisms. A familiar name or appearance does not qualify a device that works differently.

The source must already be loaded in the map you are editing. The patcher does not load another scene to find it. If the named source is missing or ambiguous, the patcher does not substitute another device.

### Rejected sources

The editor lists every device beneath a loaded crossing controller, including the bell player each controller generates. It explains why any device does not qualify:

| Reason | Meaning |
| --- | --- |
| it contains a bell player; add bells from a bell definition using the bell pattern | Bells use the controller-bound bell pattern; see [add a bell to an existing crossing](adding-crossing-bells.md). |
| it holds its own key-value object, which would intercept the crossing controller's keys | The copy's lamps and gates would observe that object instead of the crossing controller. |
| it holds a crossing controller; a copy would be a second crossing | A copy would duplicate a crossing. |
| it holds a detection span | A copy would duplicate detection. |
| it holds its own culler | A copy would duplicate culling. |
| observes key '...', which the crossing controller does not publish | The lamp or gate would never respond. |
| targets an object outside the source, so a copy would drive the original | A copy would drive the original device. |
| has no renderer under its targets that uses its lamp material | The lamp would show no change. |
| has no Animator or no animation clip | The gate would not move. |
| it has no lamp or gate that observes the controller's lights or gates key | It is not crossing equipment. |
| it is in a different Unity scene from the destination crossing and could unload while the copy still uses its assets | The other scene could unload the shared assets while the copy still uses them. |
| the destination crossing controller has ... key-value objects, so it cannot address the copy | The controller has no key-value object or more than one, so the copy could not be addressed unambiguously. |
| the destination crossing controller has several cullers, so it is unclear which one should control the copy's visibility | The copy could not be culled unambiguously. |
| the patcher added this equipment; choose a device already present in the map | Copy the map's own device instead. |
| a script in its hierarchy is missing | The device cannot be checked or copied safely. |

## What an addition does

- The patcher places the copy beneath the crossing controller. Its lamps and gates find the controller's key-value object. They follow the same lights and gates state as the native equipment, driven by the same circuits.
- The copy joins the controller's culler and takes its current culling state. It is shown and hidden with the crossing's own equipment. A culled copy initializes when it becomes visible.
- The copy shares the source's model, materials and animation. These assets belong to the scene that contains both the source and crossing, so they stay loaded while the copy exists. The copy keeps working when the source object is culled or removed from view. Removing the copy destroys only the objects the patcher created, leaving the shared assets and the working source intact.
- Each addition is an added object. The outliner lists it once in the **Added light or gate** family, marked with `+`, and its state line shows **+ ADDED**. When two or more added lights or gates at the same crossing share a name, their display names end in `(added, N of M)`, such as `Signal 1 (added, 2 of 3)`. The copy's parts have no separate entries. Culling, initialization and Undo/Redo steps for the copy do not mark the inventory stale. Edit the addition on its crossing's **Connections** tab.
- An addition that fails its check blocks only equipment edits of that crossing until the world reloads. Coverage and detection edits of the same crossing stay available.
- The crossing object, its coverage and its native equipment do not change.
- Each addition needs an ID that is unique in your scene patch file. Only one modification may use it. If two modifications share an ID, both fail. The editor refuses an ID that another modification already uses. IDs are scoped to your mod, so another supplying mod can use the same ID without a conflict. Your scene patch file never moves or removes another mod's additions.

## Add equipment in the native map editor

1. Open the Scene patches window and inspect the crossing controller. Find it in the outliner's **Crossing controller** family or select it in the world.
2. Open the **Connections** tab. **Lights, gates and bells** lists the current additions. **Add a light or gate** lists the devices, with a reason for each one that does not qualify. **Why a device is rejected** opens the table above.
3. Select **Use** for an eligible device. Each button names the device and its crossing, such as **Use Signal 1 at Allen**. The **Light or gate** form opens at the top of the tab. It proposes an unused **Equipment ID**, adding a numeric suffix if the name is taken. The proposed placement uses the device's position relative to its own crossing at the inspected crossing, offset by five metres. Change the **Equipment ID**, **Position** in map coordinates and **Rotation** in degrees as needed.
4. Select **Preview** at the bottom of the tab. The **Preview** section shows what will be added, moved, rebuilt or removed. Nothing changes yet.
5. Select **Apply** at the bottom of the tab. Apply uses the previewed change; preview again after you change a field.

To move an addition, select **Edit** beside its ID, change the fields, preview and apply. To remove one, select **Edit**, then **Remove** at the bottom of the tab and apply.

**Remove modification** in the footer removes all of your mod's coverage, detection additions, lights, gates and bells for this crossing. Native Undo and Redo reverse and repeat each applied change, including rebuilding a removed addition.

**Lights, gates and bells** shows whether each addition is visible with its lamp and gate observers registered, awaiting initialization, or culled with its crossing. If the patcher cannot build an addition from your scene patch file, for example because its source is missing, it appears as **Not built** with a reason. Select **Edit** to correct or remove it.

Save writes the additions to `Data/scene-patches.json` and raises the patcher dependency in `Info.json` to `>=0.5.0`. The game does not save the copies in native map data. The patcher rebuilds them from the scene patch file on each load. If copies already exist on a repeated load, the patcher reconciles them without duplicating them.

If an edit fails, the message area reports whether the prior equipment was restored. When cleanup is incomplete, the crossing refuses further edits until **Recheck** in the footer succeeds or the native map editor reloads.

## Write equipment in the scene patch file

Target the crossing controller as you would for coverage, using `Track.Crossings.GradeCrossingController` for `component` and `crossing-coverage-v1` for `structure`. Put each addition under its ID in `relationships.crossingEquipment`. Put the modification itself under its ID in `modifications`:

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

The scene, paths and coordinates are placeholders. Use the values the editor shows for your crossing and source. Each addition belongs to the controller targeted by its modification. An entry's key is its ID, so it has no `id` member. See the [scene patch file v1 reference](scene-patch-file-v1.md#crossing-equipment) for the field rules.

## Limits

- Additions have a fixed map placement. Track-following placement, the transform gizmo and snapping are not available for additions.
- These edits do not change the placement, rotation or activity of the crossing's native equipment.
- Copies have the colliders of their source. The patcher adds no collision handling.
- The pattern and its checks come from Railroader 0.6.2 build 26335745 and one demonstrated combined light/gate device. Expected native behavior in other builds and for other devices is based on these checked mechanisms, without an individual trial of every device.
