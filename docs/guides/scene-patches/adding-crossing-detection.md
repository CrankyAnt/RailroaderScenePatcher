<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Add detection for extra tracks at an existing crossing

When you add a track through an existing grade crossing, add detection so the crossing can detect trains on that track. You can do this in the native map editor or in `Data/scene-patches.json`.

To move a circuit from an old route to another track, or to detach a circuit, see [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md). Adding detection doesn't create a crossing or change lights, gates, bells, models, signals, CTC, or interlocking.

## What an addition is

A crossing controller evaluates circuits, each with an approach A span, an island span, and an approach B span. A detection addition is another circuit with three spans of its own. The patcher builds it, registers its spans in the track graph, and adds it after the controller's native circuits.

Define an addition with an ID, tracks, and an area:

| Part | Meaning |
| --- | --- |
| ID | A name you choose. It is unique in your mod's scene patch file and stays the same across loads. Another mod can use the same ID without affecting yours. |
| Tracks | The track segments the three spans lie on. You name each segment. |
| Area | The distances along those segments where each span starts and ends. |

You must specify the tracks and area. The patcher doesn't choose them from nearby track, and an addition detects only tracks you name.

## What stays the same

- The crossing's native circuits keep their spans, their order, and their function. You can still [correct or detach them](correcting-crossing-coverage.md) in the same modification.
- The crossing object, its equipment, and its activity do not move or change. The modification accepts no `placement` or `active`.
- The crossing keeps at least one circuit or addition that detects trains. An addition counts, so you can detach every native circuit while an addition remains. Deleting the last one is refused.

## Add detection in the native map editor

1. Open the Scene Patcher panel and inspect the crossing controller. Select it under **Grade crossing** in the outliner or click **Select in world** to select it in the world.
2. On the **Connections** tab, under **Add detection**, enter an ID for the addition in **Detection addition ID** and click **Add detection** beside it.
3. Each of **Approach A**, **Island**, and **Approach B** has two ends, **From** and **To**. For each end, enter a segment ID, the segment end (`a` or `b`) the distance is measured from, and the distance in meters. The two ends of a role can lie on different segments; the detection span covers the route between them. **From** is the end next to the island for each approach, and the island's lower end for the island. To fill an end from the world, click **Segment** next to that end, click the track, then click **Confirm**. The end takes the segment and the distance of the click from end `a`; the other end keeps its values. When editing an existing addition, the end's current segment is excluded from its choice list. You can pick segments used by other roles.
4. Click **Preview** at the bottom of the tab. The **Preview** section lists every native circuit as retained and the addition as new with its tracks and area. Nothing changes yet.
5. Click **Apply** at the bottom of the tab. Apply uses the previewed tracks. Preview again after you change a field.

To change an addition's tracks, click **Edit detection addition** with its ID. The form opens with the addition's current From and To ends. Change the fields, preview, and apply. The patcher replaces the addition's spans and keeps its circuit.

To delete an addition, click **Edit detection addition** with its ID, then **Delete** at the bottom of the tab and apply. The patcher removes the circuit from the crossing and unregisters and removes its spans. Other additions and the native circuits keep their coverage.

**Remove modification** in the footer deletes every addition of the crossing and returns its native circuits to the coverage they have without your mod.

Native Undo and Redo reverse and repeat each applied change. Undo of an addition removes its circuit and spans. Undo of a deletion builds them again.

Save writes the addition to `Data/scene-patches.json` and raises the patcher dependency in `Info.json` to `>=0.5.0`. The game does not save the added circuit or its spans in map data. The patcher builds them from the scene patch file on every load, and a repeated load reuses what it already built.

If an edit fails, the panel reports whether the prior state was restored. When the patcher cannot remove a span it built, the crossing refuses further edits until **Recheck** in the footer succeeds or the native map editor reloads.

## Write an addition in the scene patch file

Target the crossing controller with `component` set to `Track.Crossings.GradeCrossingController` and `structure` set to `crossing-coverage-v1`. In `relationships.crossingCoverage.additions`, use the addition's ID as a key and its three spans as the value. Put the modification itself under its ID in `modifications`:

```json
"cover-bryson-everett": {
  "target": {
    "scene": "BushnellWhittier",
    "path": "World/Crossings/Bryson Everett",
    "component": "Track.Crossings.GradeCrossingController",
    "structure": "crossing-coverage-v1"
  },
  "relationships": {
    "crossingCoverage": {
      "additions": {
        "third-track": {
          "approachA": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 308 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 550 } },
          "island": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 292 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 308 } },
          "approachB": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 292 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 50 } }
        }
      }
    }
  },
  "dependsOn": []
}
```

The scene, path, and segment in this example are placeholders. Use the scene and path from the inspected crossing and the IDs of your own segments. The field rules are in the [scene patch file v1](scene-patch-file-v1.md#detection-additions).

To delete an addition, remove its member from `additions`. If it was the last one, remove `additions` too, because an empty `additions` object is invalid. An addition exists only while the scene patch file lists it. To make one modification wait for another, name it in `dependsOn`. The order of the keys in the file has no effect.

## Multiplayer, later joins and reconnects

Each player's game builds the addition in its own world when that world loads. In Railroader 0.6.3, a crossing controller computes its detection in each player's own game, from the circuits that game holds. Whether the crossing's light, gate, and bell keys also reach other players hasn't been determined.

A player who joins later or reconnects loads the map again. The patcher builds the addition once in the newly loaded world, under the same addition ID, and nothing of the earlier session stays behind. If the addition fails in one player's game, that game reports it, and the other players keep their detection. See [application on each peer](scene-patch-file-v1.md#application-on-each-peer).

These statements are based on the patcher's automated tests and the code of Railroader 0.6.3. They haven't been tested in a multiplayer session.

## Limits

- An addition ID may appear in one modification of the scene patch file. If two modifications use the same ID, both fail and the other modifications still apply.
- A missing segment, a segment ID that matches more than one segment, a distance outside its segment, and a span without length are refused before anything changes.
- The patcher does not check whether trains occupy the affected spans. Make edits in scene patch editing with those spans clear.
- The patcher does not check that your spans meet at the island or that the track is connected. It checks that each span resolves, registers, and covers track.
- A crossing whose circuits were changed by something other than this patcher is refused until the world reloads.
- Research demonstrated added detection in both directions on one crossing and build. The patcher does not check warning time.
