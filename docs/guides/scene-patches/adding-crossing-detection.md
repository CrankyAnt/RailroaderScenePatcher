<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Add detection for extra tracks at an existing crossing

This guide shows how to make an existing grade crossing detect trains on a track it does not cover yet, in the map editor or in `Data/scene-patches.json`. Use it when you add a track through a crossing. To move a circuit from an old route to another track, or to detach a circuit, see [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md). This guide does not create new crossings or change lights, gates, bells, models, signals, CTC or interlocking.

## What an addition is

A crossing controller evaluates circuits. Each circuit has an approach A span, an island span and an approach B span. A detection addition is one further circuit that the patcher builds for the crossing. It has three spans of its own, and the patcher registers them in the track graph and adds the circuit to the controller after its native circuits.

You author an addition with three things:

| Part | Meaning |
| --- | --- |
| ID | A name you choose. It is unique in your MapMod's patch document and stays the same across loads. Another MapMod can use the same ID without affecting yours. |
| Tracks | The track segments the three spans lie on. You name each segment. |
| Area | The distances along those segments where each span starts and ends. |

The patcher never takes tracks or an area from nearby track. A track gets detection only when an addition names it.

## What stays the same

- The crossing's native circuits keep their spans, their order and their function. You can still [correct or detach them](correcting-crossing-coverage.md) in the same modification.
- The crossing object, its equipment and its activity do not move or change. The modification accepts no `placement` or `active`.
- The crossing keeps at least one circuit or addition that detects trains. An addition counts, so you can detach every native circuit while an addition remains. Deleting the last one is refused.

## Add detection in the map editor

1. Open the Scene Patcher panel and inspect the crossing controller, from the inventory (family **Crossing controller**) or with **Select scene object in world**.
2. Under **Crossing coverage**, enter an ID for the addition and select **Add detection with this ID**.
3. For each of approach A, island and approach B, enter the segment ID, the end (`a` or `b`) the distances are measured from, and the from and to distances in metres. To take the segment from the world, select **Choose track in world**, click the track, confirm its identity and select **Use confirmed segment** for that role. **From** is the end next to the island for each approach, and the island's lower end for the island.
4. Select **Preview selected tracks**. The panel lists every native circuit as retained and the addition as new with its tracks and area. Nothing changes yet.
5. Select **Apply previewed coverage**. Changing a field after the preview hides the apply button until you preview again.

To change the tracks of an addition, select **Edit Added detection** with its ID, change the fields, preview and apply. The patcher replaces the addition's spans and keeps its circuit.

To delete an addition, select **Edit Added detection** with its ID, then **Preview deleting the addition** and apply. Its circuit leaves the crossing, and the patcher unregisters and removes its spans. Other additions and the native circuits keep their coverage. **Remove modification** deletes every addition of the crossing and returns its native circuits to their coverage without your MapMod.

Native Undo and Redo reverse and repeat each applied change. Undo of an addition removes its circuit and spans. Undo of a deletion builds them again.

Save writes the addition to `Data/scene-patches.json` and raises the patcher dependency in `Info.json` to `>=0.5.0`. The game does not save the added circuit or its spans in map data. The patcher builds them from the document on every load, and a repeated load reuses what it already built.

If an edit fails, the panel reports whether the prior state was restored. When the patcher cannot remove a span it built, the crossing refuses further edits until **Recheck after failed edit** succeeds or the editor reloads.

## Write an addition in the document

Target the crossing controller with `component` `Track.Crossings.GradeCrossingController` and `structure` `crossing-coverage-v1`. Under `relationships.crossingCoverage`, add an entry with `addition` and the three spans:

```json
{
  "id": "cover-bryson-everett",
  "target": {
    "scene": "BushnellWhittier",
    "path": "World/Crossings/Bryson Everett",
    "component": "Track.Crossings.GradeCrossingController",
    "structure": "crossing-coverage-v1"
  },
  "relationships": {
    "crossingCoverage": [
      {
        "addition": "third-track",
        "approachA": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 308 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 550 } },
        "island": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 292 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 308 } },
        "approachB": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 292 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 50 } }
      }
    ]
  },
  "dependsOn": []
}
```

The scene, path and segment in this example are placeholders. Use the scene and path from the inspected crossing and the IDs of your own segments. The field rules are in the [scene-patch document v1 reference](scene-patch-document-v1.md#detection-additions).

To delete an addition, remove its entry. An addition exists only while the document lists it. To make one modification wait for another, name it in `dependsOn`. The order of entries and modifications in the file has no effect.

## Limits

- An addition ID may appear in one modification of the document. If two modifications use the same ID, both fail and the other modifications still apply.
- A missing segment, a segment ID that matches more than one segment, a distance outside its segment and a span without length are refused before anything changes.
- The patcher does not check whether trains occupy the affected spans. Make editor edits with those spans clear.
- The patcher does not check that your spans meet at the island or that the track is connected. It checks that each span resolves, registers and covers track.
- The editor fields take one segment per span. A span that crosses segment boundaries can be written in the document with `from` and `to` on different segments.
- A crossing whose circuits were changed by something other than this patcher is refused until the world reloads.
- Research demonstrated added detection in both directions on one crossing and build. The patcher does not check warning time.
