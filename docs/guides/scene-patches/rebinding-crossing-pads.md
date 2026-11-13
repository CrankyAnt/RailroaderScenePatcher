<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Rebind a crossing pad and regenerate its geometry

This guide shows how to set or replace the track location of a crossing pad, and what changes with it. A crossing pad is the road surface laid across the rails at a grade crossing. Its object holds a `Track.TrackMarker` and a `Track.TrackCurveMeshBuilder`. The builder generates the pad mesh along the track curves around the marker's location, so the binding decides where the pad lies on the track.

The game generates a pad once, when the pad first comes into view or close to the camera, and does not generate it again by itself. Writing a new location alone leaves the old mesh where it was. The patcher therefore runs the game's own pad generation after every binding change, so the visible pad and its culling follow the authored location. A document with a crossing-pad modification needs patcher version 0.5.0 or later; the editor's Save writes that dependency.

## What qualifies

The patcher rebinds a pad when all of these hold:

- the object holds exactly one `Track.TrackMarker`, of type `Crossing` or `Generic`, with a non-empty marker ID, and exactly one `Track.TrackCurveMeshBuilder` with a mesh container and a mesh profile,
- the object holds no `Helpers.MilePost` and no missing script,
- no other component listens to the marker's location changes,
- a `Track.TrackRelativePosition` on the object, if present, does not make the object's activity follow a track group,
- no loaded component other than that `Track.TrackRelativePosition` and the pad builder holds a direct reference to the marker, and no new-game car placement uses it. If the patcher cannot read every field that could hold a marker, it refuses every marker until the next check,
- the graph holds one registration of the marker at its current location while the pad is enabled and active, and none otherwise.

A pad inside a protected crossing qualifies too, because the crossing controller does not read the pad's marker. The crossing's own track spans and circuits are a separate operation.

The inventory shows such an object in the `Crossing pad marker` family with the structure `crossing-pad-v1`. Any other pad lists **Pad binding unavailable** with the reason and stays inspection only.

## Set or replace the binding in the editor

1. Open **Scene patches**, select **Refresh inventory and verify stored target identities**, and select the pad's row. **Select scene object in world** also selects it, as described in [select world targets](select-world-targets.md).
2. Under **Crossing pad binding**, check the **Current** lines: the stored segment, end and distance, and each graph registration under the marker ID with its owner.
3. Enter the target segment ID, the end (`a` or `b`) and the distance in metres, or select **Use confirmed segment** after confirming a track in the world.
4. Select **Set track binding**.

The patcher checks the target before it changes anything: the segment must exist in the loaded track graph and contain the distance. A missing segment, a distance beyond the segment or an end other than `a` or `b` refuses the edit; the pad keeps its binding, registration and geometry.

After the check the patcher moves the marker's graph registration to the new location, as for a Generic marker, and then runs the game's pad generation. Generation replaces the pad mesh with one built along the track at the new location and moves the pad's culling sphere to the new mesh bounds. The game's culling then decides on the next frame whether the new mesh is shown. The pad object itself does not move.

The patcher verifies the stored location, the single graph registration, that the current mesh was generated from the authored location and the pad's current position, and that the culling sphere encloses the new mesh. Any difference fails the edit: the patcher writes the earlier location back, generates the pad again and checks that the regenerated mesh matches the mesh it captured before the edit.

## Detaching is refused

A pad without a track location has no geometry, and the game logs an error each time it tries to generate one. **Detach from track** is not offered for pads, and a document that detaches a pad fails before any change. To hide a pad, disable it.

## Placement and activity

Placement and activity are separate settings of the same modification. The pad mesh follows the track, not the pad object. After a placement change the patcher generates the pad again, so the mesh stays on the track at the bound location while the object moves. A disabled pad keeps its binding without a registration and without a culling sphere; enabling it registers it again.

A pad can also follow a track, as described in [follow a track](following-track.md). The game does not regenerate a pad after a track edit, and a track edit can change the track's curve near the pad without moving the anchor, for example when an endpoint node moves along the track. For a pad that follows a track, the patcher records the track curves the pad was generated from: the segment curves within half the pad width on either side of the marker location, which the game's pad generation reads. When those curves differ, the patcher generates the pad again, even if the pad's pose is unchanged. The editor checks this a few times a second and regenerates once the changed curves have held still between two checks, about half a second, so one track edit, including a drag, regenerates the pad once, and a track edit and its native Undo or Redo both leave the pad on the current track. While the track does not change, the check compares a short summary of the nearby track and builds no curves. A pad that is only rebound and does not follow a track keeps the game's behavior: a later track edit leaves its mesh as it was until the pad is generated again. Any edit of a pad that the patcher applies, including native Undo, Redo, clearing the binding and Remove modification, generates the pad again when the track curves it was generated from have changed, so removal returns a pad to the loaded map's binding with a mesh built from the current track.

## Undo, clear and remove

Native Undo and Redo reverse and reapply each binding edit together with its registration and a new generation, so the pad returns to the earlier mesh. **Clear authored binding** removes only the relationship from this MapMod's modification. The pad returns to the binding the loaded map gives it, with matching geometry, while an authored placement or activity stays. **Remove modification** returns binding, registration, placement, activity and geometry to the loaded map's values.

If an edit fails and the patcher cannot verify the restoration, it reports incomplete cleanup and refuses further edits of the pad until **Recheck after failed edit** succeeds or the world reloads.

## Write the binding by hand

Copy the inspected identity with **Copy inspected identity** and add a modification to `Data/scene-patches.json`, as described in [scene-patch document v1](scene-patch-document-v1.md):

```json
{
  "id": "bind-crossing-pad",
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Roads Wilmot/Crossing Unprotected/Crossing Pad",
    "component": "Track.TrackMarker",
    "structure": "crossing-pad-v1"
  },
  "relationships": {
    "trackMarker": { "segment": "Sj7c", "end": "a", "distance": 186.9477 }
  },
  "dependsOn": []
}
```

On every world load the patcher applies the binding and generates the pad from it once; a repeated application leaves current geometry alone and keeps one registration.

## Limits

The automated tests check rebinding, replacement, repair of a broken binding, repetition, refusal of missing, ambiguous and out-of-range targets and of a detach, dependency isolation, map mismatch, generation failure with restoration of the captured mesh, incomplete cleanup with guarded retry, Undo, Redo, clear, removal, Save and reload against a controlled track graph and pad builder. They do not load Railroader.

Research on Railroader 0.6.2 observed the pad Mg74 at `World/Roads Wilmot/Crossing Unprotected/Crossing Pad`: a one-metre rebind left the mesh and culling sphere unchanged for at least 17 seconds, explicit generation shifted the visible pad along the track, and generating again after restoring the binding returned the recorded vertex signature, bounds and culling sphere.

One native journey on Railroader 0.6.2 with patcher commit `a282015` exercised the same pad in the map editor of a test MapMod. The inventory listed Mg74 as a `Crossing pad marker` with `crossing-pad-v1`, binding Sj7c end a at 185.9477 and one registration owned by the marker, and offered no detach. Setting the binding to 186.9477 shifted the visible pad about one metre along the track while the pad object kept its position, and a ten-metre check shifted it clearly. The graph then held one registration at the new location. A missing segment and a distance beyond the segment were refused without change. Native Undo returned the pad to its baseline alignment on the road and Redo restored the rebound pad. Remove modification returned the baseline and its Undo the rebound pad. After Save and an editor reload, the document held one modification and the pad showed the saved binding with one registration. While the inspected pad stayed in view during camera movement, the inventory did not become stale because of the pad.

A second native journey with patcher commit `ab2816f` compared the pad's geometry numerically, with the camera at the pad. Before the edit the pad held one generated mesh object with 1300 vertices, and its culling sphere matched the mesh bounds. The rebinding to 186.9477 changed the vertex hash and moved the bounds and the culling sphere 0.993 metres along the track; the sphere stayed aligned with the new bounds and the radius stayed 4.307 metres. Native Undo, and Redo followed by Remove modification, each restored the original vertex hash, bounds and culling sphere exactly. A pad far from the camera held no generated mesh yet, as the game generates it only when it comes near.

Neither journey counted the generated mesh objects after the reload. They cover that pad, build and fixture only. Other pads and multiplayer are not covered.

The mesh comparison covers the vertex positions, vertex count, bounds and mesh position of the generated renderers. Triangle indices, materials and other renderer properties are not compared.
