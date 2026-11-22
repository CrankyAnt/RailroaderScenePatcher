<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Correct the detection coverage of an existing crossing

Use the native map editor or `Data/scene-patches.json` to change which tracks an existing grade crossing detects or to detach one of its circuits. This guide explains how to move detection from an old route to tracks you choose.

For tracks the crossing does not cover yet, see [add detection for extra tracks at an existing crossing](adding-crossing-detection.md). To add equipment, see [add lights and gates to an existing crossing](adding-crossing-equipment.md) or [add a bell to an existing crossing](adding-crossing-bells.md).

Coverage edits apply only to existing crossings. They don't create crossings or change bells, models, signals, CTC, or interlocking.

## How a crossing detects trains

A crossing controller evaluates one or more circuits. Each circuit has three detection spans: approach A, the island, and approach B. A train in an approach span starts the warning; a train on the island keeps the gates down. The patcher names a circuit by the ID of the map's own island span, such as `Pipr`, because that ID identifies the circuit in the map.

Each circuit has one of three coverage settings:

| Setting | Effect |
| --- | --- |
| Omitted | The circuit keeps its coverage. This mod does not touch it. |
| Selected tracks | The circuit covers the tracks you name. The patcher creates its own three spans for them. |
| Detached | The crossing stops evaluating the circuit. |

The patcher never assigns coverage by proximity. The crossing detects a nearby track only when you select it.

## What stays the same

- The map's own detection spans for a corrected or detached circuit stay registered and unchanged. A track that used to trigger the crossing still exists and other consumers still see its spans. The crossing stops evaluating them, so a train on the old route no longer starts the warning.
- Circuits you do not edit keep their coverage and keep working in both travel directions.
- The crossing object, its equipment, and its activity do not move or change. A crossing-coverage modification accepts no `placement` or `active`.
- At least one circuit or [detection addition](adding-crossing-detection.md) keeps detection. Detaching every circuit of a crossing without an addition is refused, also when earlier edits or modifications already detached the others. In the native map editor, **Detach** is unavailable for the last covered circuit and its tooltip says why; a scene patch file that would detach it fails that modification and leaves the coverage unchanged.

## Edit coverage in the native map editor

1. Open the Scene Patcher panel and inspect the crossing controller, either from the outliner (folder **Crossing controller**) or with **Select in world**.
2. On the **Connections** tab, under **Crossing coverage**, read each circuit's current coverage, then click **Edit circuit** for the circuit to change.
3. For each of **Approach A**, **Island**, and **Approach B**, enter a segment ID, the end (`a` or `b`) the distances are measured from, and the **From** and **To** distances in meters. To fill a role's segment from the world, click **Segment** next to the role, click the track, then click **Confirm**. The role's current starting segment is excluded from its choice list. You can pick segments used by other roles. **From** is the end next to the island for each approach, and the island's lower end for the island.
4. Click **Preview** at the bottom of the tab. The panel shows the intended relationship for every circuit, including the selected tracks, which of the map's detection spans stop triggering the crossing, and which circuits are retained. Nothing changes yet.
5. Click **Apply** at the bottom of the tab. Apply uses the previewed tracks. Preview again after you change a field.

To detach a circuit, click **Detach** at the bottom of the tab and apply it. To return a circuit to its coverage without this mod, click **Clear circuit** at the bottom of the tab and apply it. **Remove modification** in the footer returns every circuit of the crossing at once. Native Undo and Redo reverse and repeat each applied change, including the spans the patcher created.

Save writes the coverage to `Data/scene-patches.json` and raises the patcher dependency in `Info.json` to `>=0.5.0`. The game does not save the patcher's spans in map data; the patcher rebuilds them from the scene patch file on every load, so a reload never duplicates them.

A crossing controller reports a problem if one of its circuit spans refers to a track segment that no longer exists, for example after track is removed or rebuilt. The message identifies the circuit by its name on the **Connections** tab, the span by its role and ID, such as `Circuit Pipr Approach A span Pdlh`, and the missing segment. To repair it, open the controller's **Connections** tab and select tracks for the circuit or detach it, as described above. If the span belongs to a [detection addition](adding-crossing-detection.md), such as `Detection addition siding Island span …`, select other tracks for the addition or delete it. If the track graph cannot be read, the controller shows a single inspection note beginning `Circuit span validity unknown` instead of reporting a problem with the track.

If an edit fails, the panel reports whether the prior coverage was restored. When cleanup is incomplete, the crossing refuses further edits until **Recheck** in the footer succeeds or the native map editor reloads.

## Write coverage in the scene patch file

Target the crossing controller with `component` `Track.Crossings.GradeCrossingController` and `structure` `crossing-coverage-v1`. In the `circuits` object of `relationships.crossingCoverage`, use the ID of the map's own island span as the key for each circuit you set. Put the modification itself under its ID in `modifications`:

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
      "circuits": {
        "Pipr": {
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

The scene, path, and segment in this example are placeholders. Use the scene, path, and circuit IDs from the inspected crossing. The field rules are in the [scene patch file v1](scene-patch-file-v1.md#crossing-coverage).

## Multiplayer, later joins and reconnects

Each player's game applies the coverage to its own crossing when its world loads. In Railroader 0.6.3, a crossing controller computes its detection in each player's own game, from the circuits that game holds. Whether the crossing's light, gate, and bell keys also reach other players hasn't been determined.

A player who joins later or reconnects loads the map again. The patcher applies each circuit once in the newly loaded world, and the circuits you did not name keep the map's own detection spans. If the coverage fails in one player's game, that game reports it, and the other players keep their coverage. See [application on each peer](scene-patch-file-v1.md#application-on-each-peer).

These statements are based on the patcher's automated tests and the code of Railroader 0.6.3. They haven't been tested in a multiplayer session.

## Limits

- The patcher does not check whether trains occupy the affected spans, and it applies coverage while a train is parked on them. Coverage edits were validated with the affected spans clear of trains; make edits in scene patch editing with those spans clear.
- A circuit named in the scene patch file must exist on the crossing in the loaded map. A circuit that does not exist, a missing segment, a segment ID that matches more than one segment, and a distance outside its segment are refused before anything changes; other modifications still apply.
- A crossing whose circuits were changed by something other than this patcher, or whose detection spans from the map are no longer registered, is refused until the world reloads.
- The scene patch editing fields take one segment per span. A span that crosses segment boundaries can be written in the scene patch file with `from` and `to` on different segments.
- Detection was demonstrated on one crossing and build in research; the patcher checks span registration and crossing coverage, not warning time.
