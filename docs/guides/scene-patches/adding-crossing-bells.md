<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Add a bell to an existing crossing

You can add a bell to an existing grade crossing in the native map editor or in `Data/scene-patches.json`. The patcher creates a separate bell player beneath the crossing controller, using the bell definition that the game uses at grade crossings. The added bell rings together with the crossing's own bell.

Adding a bell doesn't create a crossing, change the crossing's own bell, or edit a sound. Lights and gates use a different pattern. See [add lights and gates to an existing crossing](adding-crossing-equipment.md).

## Which bell definitions qualify

You can choose only bells that the game uses at grade crossings. In the base game, the crossing controllers load one bell: US&S Teardrop Crossing Bell (`rr/bell-uss-teardrop-crossing-rh`). The editor doesn't list other bell definitions, such as EMD Bronze Bell, CN Brass Rope Bell, or a bell added by another mod, even if they meet the requirements below.

A crossing bell definition qualifies for the `crossing-bell-v1` pattern only if the patcher can use the game's mechanism for creating crossing bells with that definition. It must meet these requirements:

- The game's content library contains the definition, with kind `Bell`.
- It names an audio asset, and that asset resolves in the content library.
- Its volume is a finite number greater than 0. Any audio indexes are finite times greater than 0, in strictly increasing order.
- The destination crossing controller has exactly one key-value object, on the controller's own object, and publishes its bell key there.
- The destination crossing controller is active.

The player splits the audio clip at its indexes. Each part must contain at least one sample. The audio loads after you apply the bell, so the patcher can check the parts only then. If the clip doesn't fit its indexes or the audio fails to load, the bell exists but stays silent. Its state explains why. The game's log records one warning for the bell, attributed to your mod's `Data/scene-patches.json`. The warning names the bell's ID, crossing controller, and reason. Remove the bell or replace its definition.

If the game's crossing bell definition is missing from the content library or doesn't meet these requirements, **Add a bell** explains why it can't be used. The patcher won't offer another bell instead.

The definition must already be in the content library. The patcher doesn't load a scene or another mod to find it. If the named definition is missing, it doesn't substitute another sound.

### Ineligible bell definitions

The editor lists the bell definitions that the game uses at grade crossings and explains why any are ineligible. Some reasons appear only when you apply a bell or after its audio loads:

| Reason | Meaning |
| --- | --- |
| The content library has no definition for the game's crossing bell ..., so you can't add a bell. | The definition that the game's crossing controllers load is missing. The patcher won't offer another bell instead. |
| it is a ... definition, not a bell definition | The identifier names another kind of definition, such as a horn. |
| the game doesn't use this bell at grade crossings | The scene patch file names another bell definition, such as a locomotive bell or one added by another mod. On the crossing's **Connections** tab, edit the addition to select a listed bell, or remove it. |
| its definition does not specify an audio asset | There is no sound to load. |
| its audio asset ... cannot be found in the content library | The content that supplies the sound is not available. |
| its volume ... must be a finite number greater than 0 | The bell would be silent or unusable. |
| its audio indexes must be finite times greater than 0, in strictly increasing order | The player could not split the clip. |
| the destination crossing controller has ... key-value objects, so it cannot address the bell | The controller has no key-value object or more than one, so the bell could not be addressed unambiguously. |
| the destination crossing controller is inactive, so an added bell would not start | A bell beneath it would not start, and its audio could not be released when the scene unloads. |
| Crossing bells are unavailable in this version of the game. | This game version lacks something the bell pattern needs. Lights and gates stay available. The editor shows this reason in **Add a bell**. With **Debug logging** on, the Player log has the technical details. |
| The patcher couldn't read the bell definitions from the game's content library, so it can't list bells. Report this to the patcher maintainer and include the Player log. | The Player log has a warning. With **Debug logging** on, it also has the technical details for a report to the patcher maintainer. |
| bell definition ... is unavailable in the content library | The named definition is missing from the content library. Edit this addition on the crossing's Connections tab to select another bell definition, or remove the addition. The patcher does not choose a replacement. |
| is ambiguous; ... bell definitions share its identifier | More than one bell definition matches the identifier. Edit this addition on the crossing's Connections tab to select another bell definition, or remove the addition. |
| its audio index ... s does not split its clip of ... s into parts of at least one sample | After the audio loads, the clip proves too short for the definition's indexes. The bell stays silent and a warning identifies it. |

## What a bell does

- The bell is a separate player beneath the crossing controller. Its sound comes from the position you give it, just as the crossing's own bell sounds from its position.
- The bell observes the controller's bell key. The controller sets this key in response to the crossing's circuits to ring its own bell. The added bell reads the key's current value when it is created and follows every later change, so both bells ring and stop together. The controller remains unchanged and does not know about the added bell.
- The bell loads its definition's audio through the content library and holds its own reference to that audio for as long as it exists. While the audio loads, the bell stays silent and its state shows that it is loading. Removing the bell stops it, stops it observing the bell key, cancels any unfinished load, and releases its audio reference. The crossing's own bell keeps its audio.
- Like the crossing's own bell, the added bell does not join a culler.
- Each bell is an added object. The outliner lists it once under its category, marked with `+`, and its state line shows **+ ADDED**. Its name is the bell's name followed by `(added: <Equipment ID>)`, such as `US&S Teardrop Crossing Bell (added: north-bell)`, in the outliner, in the **Lights, gates, and bells** list, and in the inspector heading. Two bells from one source differ by their Equipment IDs. Where space is limited, the editor shortens the bell's name before `(added: <Equipment ID>)`. Edit the bell on its crossing's **Connections** tab.
- A bell whose audio fails to load blocks other equipment edits of that crossing until you remove it or replace its definition. Removing or replacing it stays available, and so do coverage and detection edits of the same crossing.
- The crossing object, its coverage, and its native equipment do not change.
- Each bell needs an ID that no other bell, light, or gate in your scene patch file uses. Only one modification may use that ID. IDs are scoped to your mod, so another supplying mod can use the same ID without a conflict. Your scene patch file never moves or removes another mod's bells. Repeated loads keep one player, one observer, and one audio reference per ID.

## Add a bell in the native map editor

1. Open the Scene Patcher window and inspect the crossing controller. Find it under **Grade crossing** in the outliner or select it in the world.
2. Open the **Connections** tab. **Lights, gates, and bells** lists the current additions. Under **Add a bell**, choose from the definitions the game uses at grade crossings. In the base game, only **US&S Teardrop Crossing Bell** is listed. The editor explains why a listed definition isn't eligible. **Why a bell definition isn't eligible** opens the table above.
3. Click **Use** for an eligible definition. Each button shows its display name, such as **Use US&S Teardrop Crossing Bell**. A form opens at the top of the tab with an unused **Equipment ID** and a position five meters beside the inspected crossing. Change the **Equipment ID**, **Position** in map coordinates, and **Rotation** in degrees as needed.
4. Click **Preview** at the bottom of the tab. The **Preview** section shows what will be added, moved, rebuilt, or removed. Nothing changes yet.
5. Click **Apply** at the bottom of the tab.

To move a bell, click **Edit** under its name, change the fields, preview, and apply. If **Add a bell** lists more than one definition, you can change the sound. Click **Use** for another definition, enter the bell's ID, and apply. The patcher rebuilds the bell with that definition. To remove a bell, click **Edit**, then **Remove** at the bottom of the tab and apply.

**Remove modification** in the footer removes all of your mod's coverage, detection additions, lights, gates, and bells for this crossing. Native Undo and Redo reverse and repeat each applied change, including rebuilding a removed bell with its audio.

**Lights, gates, and bells** shows whether each bell's audio is loading, has loaded, or has failed. It also shows whether the added bell and the crossing's own bell are ringing or quiet. If the patcher can't build a bell from your scene patch file, for example because its definition is missing, the bell appears as **Not built** with a reason. Its name is its equipment ID followed by the definition's name. When the content library doesn't hold the definition, or the definition has no name, the last part of the definition's identifier takes its place, such as `ring: bell-uss-teardrop-crossing-rh`. A bell whose player is gone also appears as **Not built**, under its own name. **Recheck** or your next equipment change builds it again from its definition.

The patcher builds all additions for a crossing together. If one addition is refused, none are built. Each of the others appears as **Not built** and names the refused addition. **Recheck** is refused for the same reason and changes nothing.

Click **Edit** to correct or remove each refused addition. To change its source, leave its **Edit** form open and click **Use** for an eligible device or bell definition. The addition keeps its equipment ID and placement. Then click **Preview** and **Apply**.

While another refused addition remains, correcting or removing an addition changes only the scene patch file. The patcher checks each correction as though the other refused additions were absent. If the modification would still be refused, for example because of the correction's own source or the crossing's detection, the patcher refuses the correction before anything changes. Once no refused additions remain, the patcher builds the additions.

If incomplete cleanup blocks the crossing, you can't edit or remove the refused addition, and **Recheck** stays refused. Reload the native map editor to clear the block, then correct or remove the addition.

Save writes the bells to `Data/scene-patches.json` and raises the patcher dependency in `Info.json` to `>=0.5.0`. The game does not save bells in native map data; the patcher rebuilds them from the scene patch file on every load.

If an edit fails, the message area reports whether the prior equipment was restored. When cleanup is incomplete, the crossing refuses further edits until **Recheck** in the footer succeeds or the native map editor reloads.

## Write a bell in the scene patch file

Target the crossing controller as for coverage, and key the bell by its ID under `relationships.crossingEquipment`, beside any lights and gates:

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
      "east-bell": {
        "pattern": "crossing-bell-v1",
        "source": { "definition": "rr/bell-uss-teardrop-crossing-rh" },
        "placement": { "mode": "fixed", "position": [4630.5, 529.2, 5462.1], "rotation": [0, 0, 0, 1] }
      }
    }
  },
  "dependsOn": []
}
```

The scene, path, and coordinates are placeholders. Use the values the editor shows for your crossing. Set `source.definition` to the identifier of a bell definition that the game uses at grade crossings. The patcher refuses a bell that names another definition. The bell observes the bell key of the controller targeted by its modification. See the [scene patch file v1 reference](scene-patch-file-v1.md#crossing-equipment) for the field rules.

## Multiplayer, later joins and reconnects

When a player's world loads, their game builds the bell beneath its own crossing controller and loads the audio. In Railroader 0.6.3, each player's crossing controller computes detection locally and sets its bell key from that detection. The added bell follows that local controller's bell key. Whether the key also reaches other players hasn't been determined.

A player who joins later or reconnects loads the map again. The patcher builds each bell once in the newly loaded world, under the same equipment ID. The bell of the earlier session released its audio when the map unloaded. If a bell fails in one player's game, for example because its bell definition is missing there, that game reports it, and the other players keep their bell. See [application on each peer](scene-patch-file-v1.md#application-on-each-peer).

These statements are based on the patcher's automated tests and the code of Railroader 0.6.3. They haven't been tested in a multiplayer session.

## Limits

- Bells have a fixed map placement. Track-following placement, the transform gizmo, and snapping are not available for bells.
- The crossing's own bell is not edited, moved, or silenced.
- In Railroader 0.6.3, an added bell has the same audio priority as the crossing's own bell.
- The pattern and its checks come from Railroader 0.6.2 build 26335745. In that build, the crossing controller's bell and the locomotive bell load their definitions through the same route. The bell sources come from Railroader 0.6.3, where the crossing controller loads `rr/bell-uss-teardrop-crossing-rh`.
