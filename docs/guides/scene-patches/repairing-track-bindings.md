<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Repair, replace or detach a track-marker binding

This guide shows how to set, replace or detach the track location of a Generic track marker, such as a yard-limit or station sign, and what changes with it. The binding is the location stored in the object's `Track.TrackMarker`: a segment ID, the segment end the distance counts from, and the distance in metres. The track graph registers the marker under that segment, and game code that looks the marker up by its ID finds it there.

A binding edit never moves the object. Placement and activity are separate settings of the same modification; you can change them before, after or without a binding edit. A document with a binding, or with any modification of a track-marker object, needs patcher version 0.4.0 or later; the editor's Save writes that dependency.

## What qualifies

The patcher edits a marker when all of these hold:

- the object holds exactly one `Track.TrackMarker`, of type `Generic`, with a non-empty marker ID,
- the object holds no `Helpers.MilePost` and no `Track.TrackCurveMeshBuilder`, and no parent holds a `Track.Crossings.GradeCrossingController`,
- no other component listens to the marker's location changes,
- a `Track.TrackRelativePosition` on the object, if present, does not make the object's activity follow a track group,
- no loaded component other than that `Track.TrackRelativePosition` holds a direct reference to the marker, and no new-game car placement uses it. If the patcher cannot read every field that could hold a marker, it does not know the holders and refuses every marker until the next check,
- the graph holds one registration of the marker at its current location while the marker is enabled and active, and none otherwise.

Each of these refusals names a consumer of the marker's location that a binding edit would leave behind. Objects whose activity follows a track group wait for the track-group operation that owns that subscription.

The inventory shows such an object in the `Track marker` family with the structure `track-marker-v1`. Any other marker lists **Track binding unavailable** with the reason and stays inspection only. Mileposts, crossing pads and crossings have their own later operations. Signal markers fall outside the supported operations.

## Set or replace the binding in the editor

1. Open **Scene patches**, select **Refresh inventory and verify stored target identities**, and select the marker's row. **Select scene object in world** also selects it, as described in [select world targets](select-world-targets.md).
2. Under **Track binding**, check the **Current** lines. They show the stored segment, end and distance, and each graph registration under the marker ID with its owner. The inventory lists a missing segment as a broken binding.
3. Enter the target segment ID, the end (`a` or `b`) and the distance in metres. To take the segment from the world, select **Choose replacement segment**, click the track, confirm it and select **Use confirmed segment**.
4. Select **Set track binding**.

The patcher checks the target before it changes anything: the segment must exist in the loaded track graph and contain the distance. A missing segment, a distance beyond the segment or an end other than `a` or `b` refuses the edit, and the marker keeps its binding and registration. The patcher never picks a nearby segment instead.

After the check it removes the marker's graph registration, writes the new location, and registers the marker at that location when the marker is enabled and active. A map label below the marker is registered with the map again, so its dimming follows the track group of the new location. The patcher then verifies that the stored location is the authored one, that the graph holds exactly one registration, for this marker, at that location, and that each registered map label is dimmed by the new location's track group. Any difference fails the edit and restores the earlier location and registration.

## Detach the marker

Select **Detach from track**. The stored location becomes empty and the graph holds no registration for the marker, so a lookup by its ID no longer finds it. The object stays where it is and keeps its activity. The document records an explicit `"detach": true`; leaving the relationship out of the document would leave the binding untouched.

## Undo, clear and remove

Native Undo and Redo reverse and reapply each binding edit together with its registration. **Clear authored binding** removes only the relationship from this MapMod's modification: the marker returns to the binding the loaded map gives it, while any authored placement or activity stays. **Remove modification** removes the whole modification and returns binding, registration, placement and activity to the loaded map's values. Other objects' modifications stay applied.

If an edit fails and the patcher cannot verify the restoration, it reports incomplete cleanup and refuses further edits of the marker until **Recheck after failed edit** succeeds or the world reloads. Recheck refuses while the graph registration does not match the stored location.

## Activity and registration

A disabled marker keeps its binding without a registration. When it is enabled, the game registers it at the bound location. An explicit `"active": false` in the same modification removes the registration and keeps the binding.

## Write the binding by hand

Copy the inspected identity with **Copy inspected identity** and add a modification to `Data/scene-patches.json`, as described in [scene-patch document v1](scene-patch-document-v1.md):

```json
{
  "id": "bind-station-1",
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Ela/Signs/Station Sign (1)",
    "component": "Track.TrackMarker",
    "structure": "track-marker-v1"
  },
  "relationships": {
    "trackMarker": { "segment": "S9sc", "end": "a", "distance": 225.6681 }
  },
  "dependsOn": []
}
```

Replace the `trackMarker` value with `{ "detach": true }` to detach the marker. Add `placement` or `active` to the same modification to change those as well.

## Limits

The automated tests check set, replacement, detachment, omission, refusal of missing, ambiguous and out-of-range targets, dependency isolation, failure restoration, guarded retry, Undo, Redo, removal and reload against a controlled track graph. They do not load Railroader.

The supported pattern comes from the game's source: setting a marker's location does not change its graph registration, so the patcher repeats the registration steps the marker performs when it is enabled and disabled.

One native journey on Railroader 0.6.2 with patcher source `a59b8d9` exercised the yard limit Mkwd in the map editor of a test MapMod, with four other inherited signs as unchanged controls. Set, replacement, detachment, refusal of a missing segment and of a distance beyond the segment, a placement change, native Undo and Redo, Clear authored binding, physical snap, Save, editor reload and removal behaved as described above. Each bound state had exactly one graph registration owned by the marker and a matching graph lookup, a detached marker had none, and the controls kept their bindings, registrations and poses. That journey did not cover a completed transform-gizmo placement of a bound marker, which it refused as a changed target, a map-label refresh, because no admitted sign has a registered map label, or a second application within one load. It covers that marker, build and fixture only.

The game reads a Generic marker's binding in these places:

- Track scripts look a marker up by ID in the graph registration, and fall back to the marker's stored location.
- The map dims a label below a marker by the track group of the marker's location, cached when the label registers. The patcher refreshes that cache.
- A new-game car placement reads its marker's location when the setup runs. The patcher refuses such a marker.
- The flare and planner queries read registrations of their own marker types, not of Generic markers.
- `Track.TrackRelativePosition` moves its object only when its relative placement is configured again. In the game, map objects do that, not the inherited scene signs.

The inherited Generic signs in the research inventory are the whistle signs M5pp near Sylva and Mm8w at `World/Large Scenery/Ela/Crossing Unprotected - Bryson Everett/Whistle Sign E`, the speed sign Mwp1 at `World/Large Scenery/Alarka Signs/Speed Sign`, the yard limit Mkwd at `World/Large Scenery/Alarka Signs/Yard Limit` and the station sign Mi4f at `World/Large Scenery/Ela/Signs/Station Sign (1)`. In that journey's inventory, M5pp, Mm8w, Mkwd and Mi4f carried `track-marker-v1`; Mwp1's activity follows its track group, so it is refused and stays inspection only. Only Mkwd was edited; the other admitted signs are unverified after a binding edit, a Save and a reload. Segment IDs are unique keys of the loaded track graph, so the game itself has no ambiguous segment target. The ambiguity refusal guards the document contract.

A track-relative object does not move when its marker is rebound, and its terrain snap does not run. Objects whose activity follows a track group are refused, because rebinding would also move that group subscription. Multiplayer is not covered.
