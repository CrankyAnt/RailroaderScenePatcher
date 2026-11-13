<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Correct the detection coverage of an existing crossing

This guide shows how to move the train detection of an existing grade crossing from an old route to the tracks you choose, or how to detach a circuit, in the map editor or in `Data/scene-patches.json`. It applies to existing crossings only. It does not create new crossings or change lights, gates, bells, models, signals, CTC or interlocking.

## How a crossing detects trains

A crossing controller evaluates one or more circuits. Each circuit has three detection spans: approach A, the island and approach B. A train in an approach span starts the warning; a train on the island keeps the gates down. The patcher names a circuit by the ID of its native island span, such as `Pipr`, because that ID identifies the circuit in the map.

Each circuit has one of three coverage settings:

| Setting | Effect |
| --- | --- |
| Omitted | The circuit keeps its coverage. This MapMod does not touch it. |
| Selected tracks | The circuit covers the tracks you name. The patcher creates its own three spans for them. |
| Detached | The crossing stops evaluating the circuit. |

The patcher never assigns coverage by proximity. A track near the crossing gets detection only when you select it.

## What stays the same

- The native spans of a corrected or detached circuit stay registered and unchanged. A track that used to trigger the crossing still exists and other consumers still see its spans. The crossing simply stops evaluating them, so a train on the old route no longer starts the warning.
- Circuits you do not edit keep their coverage and keep working in both travel directions.
- The crossing object, its equipment and its activity do not move or change. A crossing-coverage modification accepts no `placement` or `active`.
- At least one circuit keeps detection. Detaching every circuit is refused, also when earlier edits or modifications already detached the others. In the editor, **Preview detach** is unavailable for the last covered circuit and the panel says why; a document that would detach it fails that modification and leaves the coverage unchanged.

## Edit coverage in the map editor

1. Open the Scene Patcher panel and inspect the crossing controller, either from the inventory (family **Crossing controller**) or with **Select scene object in world**.
2. Under **Crossing coverage**, read the current coverage of each circuit, then select **Edit circuit** for the circuit to change.
3. For each of approach A, island and approach B, enter a segment ID, the end (`a` or `b`) the distances are measured from, and the from and to distances in metres. To take the segment from the world, select **Choose track in world**, click the track, confirm its identity and select **Use confirmed segment** for that role. **From** is the end next to the island for each approach, and the island's lower end for the island.
4. Select **Preview selected tracks**. The panel shows the intended relationship for every circuit: the selected tracks, which native spans stop triggering the crossing, and which circuits are retained. Nothing changes yet.
5. Select **Apply previewed coverage**. Changing a field after the preview hides the apply button until you preview again.

To detach a circuit, select **Preview detach** and apply it. To return a circuit to its coverage without this MapMod, select **Preview clearing the authored circuit** and apply it. **Remove modification** returns every circuit of the crossing at once. Native Undo and Redo reverse and repeat each applied change, including the spans the patcher created.

Save writes the coverage to `Data/scene-patches.json` and raises the patcher dependency in `Info.json` to `>=0.5.0`. The game does not save the patcher's spans in map data; the patcher rebuilds them from the document on every load, so a reload never duplicates them.

If an edit fails, the panel reports whether the prior coverage was restored. When cleanup is incomplete, the crossing refuses further edits until **Recheck after failed edit** succeeds or the editor reloads.

## Write coverage in the document

Target the crossing controller with `component` `Track.Crossings.GradeCrossingController` and `structure` `crossing-coverage-v1`, and list the explicit circuits under `relationships.crossingCoverage`:

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
        "circuit": "Pipr",
        "approachA": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 308 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 550 } },
        "island": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 292 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 308 } },
        "approachB": { "from": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 292 }, "to": { "segment": "SCrankyAntDetection21", "end": "a", "distance": 50 } }
      }
    ]
  },
  "dependsOn": []
}
```

The scene, path and segment in this example are placeholders; use the scene, path and circuit IDs from the inspected crossing. The field rules are in the [scene-patch document v1 reference](scene-patch-document-v1.md#crossing-coverage).

## Limits

- The patcher does not check whether trains occupy the affected spans, and it applies coverage while a train is parked on them. Coverage edits were validated with the affected spans clear of trains; make editor edits with those spans clear.
- A circuit named in the document must exist on the crossing in the loaded map. A circuit that does not exist, a missing segment, a segment ID that matches more than one segment and a distance outside its segment are refused before anything changes; other modifications still apply.
- A crossing whose circuits were changed by something other than this patcher, or whose native spans are no longer registered, is refused until the world reloads.
- The editor fields take one segment per span. A span that crosses segment boundaries can be written in the document with `from` and `to` on different segments.
- Detection was demonstrated on one crossing and build in research; the patcher checks span registration and track coverage, not warning time.
