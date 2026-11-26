<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Correct the detection coverage of an existing crossing

Use the native map editor or `Data/scene-patches.json` to change which tracks an existing grade crossing detects or to detach one of its circuits. This guide explains how to move detection from an old route to tracks you choose.

For tracks the crossing does not cover yet, see [add detection for extra tracks at an existing crossing](adding-crossing-detection.md). To add equipment, see [add lights and gates to an existing crossing](adding-crossing-equipment.md) or [add a bell to an existing crossing](adding-crossing-bells.md).

Coverage edits apply only to existing crossings. They don't create crossings or change bells, models, signals, CTC, or interlocking.

## How a crossing detects trains

A crossing controller evaluates one or more circuits. Each circuit has three detection spans: approach A, the island, and approach B. A train in an approach span starts the warning. A train on the island keeps the gates down. The patcher names each circuit by the ID of the map's own island span, such as `Pipr`, which identifies that circuit in the map.

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

1. Open the Scene Patcher panel and inspect the crossing controller. Select it under **Grade crossing** in the outliner or click **Select in world** to select it in the world.
2. On the **Connections** tab, under **Crossing coverage**, read each circuit's current coverage, then click **Edit circuit** for the circuit to change.
3. The form opens with each role's current detection span filled in, also for a circuit your mod doesn't change yet. A detached circuit opens with the map's own detection spans of that circuit: the track it covered before it was detached. Each of **Approach A**, **Island**, and **Approach B** has two ends, **From** and **To**. For each end, enter a segment ID, the segment end (`a` or `b`) the distance is measured from, and the distance in meters. The two ends of a role can lie on different segments; the detection span covers the route between them. **From** is the end next to the island for each approach, and the island's lower end for the island. To fill an end from the world, click **Segment** next to that end, click the track, then click **Confirm**. The end takes the segment and the distance of the click from end `a`; the other end keeps its values. The end's current segment is excluded from its choice list. You can pick segments used by other roles.
4. Click **Preview** at the bottom of the tab. The panel shows the intended relationship for every circuit, including the selected tracks, which of the map's detection spans stop triggering the crossing, and which circuits are retained. Nothing changes yet.
5. Click **Apply** at the bottom of the tab. Apply uses the previewed tracks. Preview again after you change a field.

If you apply the form of a native or edited circuit without a change, nothing changes: a native circuit keeps the map's own detection spans, and the scene patch file stays as it is. For a detached circuit, applying the form without a change gives the circuit the map's own spans as selected tracks, and the crossing evaluates it again. To return a detached circuit to the map's own detection spans, click **Clear circuit** instead.

To detach a circuit, click **Detach** at the bottom of the tab and apply it. To return a circuit to its coverage without this mod, click **Clear circuit** at the bottom of the tab and apply it. **Remove modification** in the footer returns every circuit of the crossing at once. Native Undo and Redo reverse and repeat each applied change, including the spans the patcher created.

Save writes the coverage to `Data/scene-patches.json` and raises the patcher dependency in `Info.json` to `>=0.5.0`. The game does not save the patcher's spans in map data; the patcher rebuilds them from the scene patch file on every load, so a reload never duplicates them.

A crossing controller reports a problem if one of its circuit spans refers to a track segment that no longer exists, for example after track is removed or rebuilt. The message names the missing segment and identifies the circuit by its name on the **Connections** tab and the span by its role and ID, such as `Circuit Pipr Approach A span Pdlh`.

To repair the problem, open the controller's **Connections** tab and select tracks for the circuit or detach it, as described above. If the span belongs to a [detection addition](adding-crossing-detection.md), such as `Detection addition siding Island span …`, select other tracks for the addition or delete it.

If the patcher can't read the track graph, the controller shows one inspection note beginning `Circuit span validity unknown`. It doesn't report a problem with the track.

If an edit fails, the panel reports whether the prior coverage was restored. When cleanup is incomplete, the crossing refuses further edits until **Recheck** in the footer succeeds or the native map editor reloads.

### Detection that no longer fits its track

Moving a track node in the Map Editor keeps the segment IDs, so a span still refers to existing segments. Its stored distances don't move with the track, though. When you inspect a crossing controller, the patcher checks the detection spans of its circuits against the current track for the two faults that the game itself checks or reports:

- **An end outside its track segment**: the stored distance of a span end is longer than its segment, for example after the segment got shorter. The game detects no train on the span.
- **No track route between the span's ends**: the game finds no route from one end of the span to the other. The game logs that the span has no route, gives it length zero, and detects no train on it.

The patcher checks nothing that the game doesn't check. It doesn't check the length of a route that the game accepts, or whether an approach starts next to its island.

Each fault is a problem with a plain explanation and its repair route: on the **Connections** tab, click **Edit circuit** and select tracks for the circuit or detach it, or click **Edit detection addition** and select other tracks for the addition or delete it. A span on a segment that no longer exists is reported once, as described above, and not as a fault.

After a track node moves, the game keeps the route it already built for a span until the span is edited or the world loads again. The patcher checks the route on the current track, which is the route the game builds after a repair or the next load.

A crossing controller that the patcher can't edit, such as a crossing object with a missing script, has no repair route. All its circuit findings, a missing span included, appear under **Information** on the **Object** tab with the reason, and not as problems.

The checks read only the current scene: the patcher stores no earlier positions or distances, and inspecting changes nothing. Whether the island still covers the road is not checked; compare it with the world.

## Write coverage in the scene patch file

Target the crossing controller with `component` set to `Track.Crossings.GradeCrossingController` and `structure` set to `crossing-coverage-v1`. In `relationships.crossingCoverage.circuits`, key each circuit you set by the ID of the map's own island span. Put the modification itself under its ID in `modifications`:

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
- Detection was demonstrated on one crossing and build in research; the patcher checks span registration and crossing coverage, not warning time.
