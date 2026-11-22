<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Rebind a crossing pad and regenerate its geometry

A crossing pad is the road surface laid across the rails at a grade crossing. To move it in the native map editor, set or replace its track binding. This guide explains how to change that binding and what happens to the pad's geometry.

The pad object holds a `Track.TrackMarker` and a `Track.TrackCurveMeshBuilder`. The builder generates the pad mesh along the track curves around the marker's location, so the binding determines where the pad lies. The **Placement** tab shows the object's placement, but **Numeric**, **Gizmo**, **Snap**, and **Follow** are inactive for pads. None of these modes changes the binding used to generate the geometry.

The game generates a pad once, when the pad first comes into view or close to the camera, and does not generate it again by itself. Writing a new location alone leaves the old mesh where it was. The patcher therefore runs the game's own pad generation after every binding change, so the visible pad and its culling follow the mod's location. A scene patch file with a crossing-pad modification needs patcher version 0.5.0 or later; the native map editor's Save writes that dependency.

## What qualifies

The patcher can rebind a pad only when it meets all these requirements:

- The object holds exactly one `Track.TrackMarker`, of type `Crossing` or `Generic`, with a non-empty marker ID, and exactly one `Track.TrackCurveMeshBuilder` with a mesh container and a mesh profile.
- The object holds no `Helpers.MilePost` and has no missing script.
- No other component listens to the marker's location changes.
- If the object has a `Track.TrackRelativePosition`, that component doesn't make the object's activity follow a track group.
- No loaded component other than that `Track.TrackRelativePosition` and the pad builder holds a direct reference to the marker, and no new-game car placement uses it. If the patcher can't read every field that could hold a marker, it refuses every marker until the next check.
- While the pad is enabled and active, the marker has exactly one named location, which matches its stored track location. Otherwise, the marker has no named location.

A pad inside a protected crossing qualifies too, because the crossing controller does not read the pad's marker. The crossing's own track spans and circuits are a separate operation.

The inventory shows such an object in the `Crossing pad marker` family with the structure `crossing-pad-v1`. For any other pad, **Supported edits** on the **Object** tab reads **Not editable:** with the reason. An enabled pad without a usable binding is listed under the **Problems** filter; a disabled one is not.

## Set or replace the binding in the native map editor

1. Open **Scene Patcher** and select the pad's row in the outliner, in the folder **Crossing pad marker**. **Select in world** also selects it, as described in [select world targets](select-world-targets.md).
2. On the **Connections** tab, check the stored segment, end, and distance under **Current binding** in **Crossing pad binding**. Each **Named location** entry shows a location registered under this marker ID and tells you if it belongs to another object.
3. In the **Track location (m)** row, enter the target segment ID in **Segment**, the end (`a` or `b`) in **End**, and the distance in meters in **Distance**. To select the segment in the world, click **Segment** in **Replacement targets**, click the track, then click **Confirm** in the footer. The confirmed segment fills the **Segment** field.
4. Click **Set** at the bottom of the tab.

The patcher checks the target before it changes anything: the segment must exist in the loaded track graph and contain the distance. A missing segment, a distance beyond the segment, or an end other than `a` or `b` refuses the edit; the pad keeps its binding, registration, and geometry.

After the check, the patcher changes the marker's named location to the new location, as for a Generic marker, and then runs the game's pad generation. Generation replaces the pad mesh with one built along the track at the new location and moves the pad's culling sphere to the new mesh bounds. The game's culling then decides on the next frame whether the new mesh is shown. The pad object itself does not move.

The patcher checks the stored track location and that the game has exactly one named location for the marker. It also verifies that the current mesh was generated from the mod's location and the pad's current position, and that the culling sphere encloses the new mesh. Any difference fails the edit: the patcher writes the earlier location back, generates the pad again, and checks that the regenerated mesh matches the mesh it captured before the edit.

## Detaching is refused

A pad without a track location has no geometry. The game logs an error each time it tries to generate one. **Detach** at the bottom of the tab is unavailable for pads, with a tooltip explaining why. A scene patch file that detaches a pad fails before anything changes. To hide a pad, disable it.

## Placement and activity

Placement and activity are separate settings of the same modification. The pad mesh follows the track, not the pad object. After a placement change the patcher generates the pad again, so the mesh stays on the track at the bound location while the object moves. A disabled pad keeps its binding without a registration and without a culling sphere; enabling it registers it again.

A pad can also follow a track, as described in [follow a track](following-track.md). The game doesn't regenerate a pad after a track edit. An edit can change the curve near the pad without moving its anchor, for example when an endpoint node moves along the track.

For a pad that follows a track, the patcher records the curves used to generate it. These are the segment curves within half the pad width on either side of the marker location, which the game's pad generation reads. If those curves change, the patcher generates the pad again, even if its placement hasn't changed.

While you edit, the patcher checks the curves a few times a second. It regenerates the pad once the changed curves stay still between two checks, about half a second. A single track edit, including a drag, therefore regenerates the pad once. Both the track edit and its native Undo or Redo leave the pad on the current track. While the track is unchanged, the check compares a short summary of the nearby track without building curves.

A pad that is only rebound, without following a track, keeps the game's behavior. A later track edit leaves its mesh unchanged until the pad is generated again. Whenever the patcher applies a pad edit, including native Undo, Redo, clearing the binding, or Remove modification, it regenerates the pad if the curves used to generate it have changed. Removing the modification therefore restores the loaded map's binding with a mesh built from the current track.

## Undo, clear and remove

Native Undo and Redo reverse and reapply each binding edit together with its registration and a new generation, so the pad returns to the earlier mesh. **Clear binding** at the bottom of the tab removes only the relationship from this mod's modification. The pad returns to the binding the loaded map gives it, with matching geometry, while the mod's placement or activity stays. **Remove modification** in the footer returns binding, registration, placement, activity, and geometry to the loaded map's values.

If an edit fails and the patcher cannot verify the restoration, it reports incomplete cleanup and refuses further edits of the pad until **Recheck** in the footer succeeds or the world reloads.

## Write the binding by hand

Copy the inspected identity with **Copy ID** in the command band and add a modification to the `modifications` object of `Data/scene-patches.json`, under a key of your choice, as described in [scene patch file v1](scene-patch-file-v1.md):

```json
"bind-crossing-pad": {
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

The automated tests check rebinding, replacement, repair of a broken binding, repetition, refusal of missing, ambiguous, and out-of-range targets and of a detach, dependency isolation, map mismatch, generation failure with restoration of the captured mesh, incomplete cleanup with guarded retry, Undo, Redo, clear, removal, Save, and reload against a controlled track graph and pad builder. They do not load Railroader.

Research on Railroader 0.6.2 observed the pad Mg74 at `World/Roads Wilmot/Crossing Unprotected/Crossing Pad`: a one-meter rebind left the mesh and culling sphere unchanged for at least 17 seconds, explicit generation shifted the visible pad along the track, and generating again after restoring the binding returned the recorded vertex signature, bounds and culling sphere.

One native journey on Railroader 0.6.2 with patcher commit `a282015` exercised the same pad in the native map editor of a test mod. The inventory listed Mg74 as a `Crossing pad marker` with `crossing-pad-v1`, binding Sj7c end a at 185.9477 and one registration owned by the marker, and offered no detach. Setting the binding to 186.9477 shifted the visible pad about one meter along the track while the pad object kept its position, and a ten-meter check shifted it clearly. The graph then held one registration at the new location. A missing segment and a distance beyond the segment were refused without change. Native Undo returned the pad to its baseline alignment on the road and Redo restored the rebound pad. Remove modification returned the baseline and its Undo the rebound pad. After Save and a native map editor reload, the scene patch file held one modification and the pad showed the saved binding with one registration. While the inspected pad stayed in view during camera movement, the inventory did not become stale because of the pad.

A second native journey with patcher commit `ab2816f` compared the pad's geometry numerically, with the camera at the pad. Before the edit the pad held one generated mesh object with 1300 vertices, and its culling sphere matched the mesh bounds. The rebinding to 186.9477 changed the vertex hash and moved the bounds and the culling sphere 0.993 meters along the track; the sphere stayed aligned with the new bounds and the radius stayed 4.307 meters. Native Undo, and Redo followed by Remove modification, each restored the original vertex hash, bounds, and culling sphere exactly. A pad far from the camera held no generated mesh yet, as the game generates it only when it comes near.

Neither journey counted the generated mesh objects after the reload. They cover that pad, build, and fixture only. Other pads and multiplayer are not covered.

The mesh comparison covers the vertex positions, vertex count, bounds, and mesh position of the generated renderers. Triangle indices, materials, and other renderer properties are not compared.
