<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Reposition or rebind a milepost

This guide shows how to move or rotate a milepost, disable it, and set, replace or detach its track binding, and what stays unchanged. A milepost is a post that shows a mile value beside the track. Its object holds a `Helpers.MilePost` with a prefix, an origin flag and a mile value, a `Track.TrackMarker` and a `Helpers.TextSynchronizer` with the displayed text.

The physical placement and the track binding are separate settings. Moving the post leaves its binding as it is, and a binding edit does not move the post. The binding is the location stored in the post's track marker: a segment ID, the segment end the distance counts from and the distance in metres. The track graph registers the marker under that segment.

No edit changes the milepost's model, mile value, prefix, origin flag or displayed text, and the patcher never recalculates a mile value. The game keeps an origin cache that maps each prefix to its origin milepost once something has looked it up. That cache holds the post itself, not its location, so an edit leaves it as it was. A scene patch file with a milepost modification needs patcher version 0.5.0 or later; the native map editor's Save writes that dependency.

## What qualifies

The patcher edits a milepost when all of these hold:

- the object holds exactly one `Helpers.MilePost`, exactly one `Track.TrackMarker` of type `Generic` with a non-empty marker ID, and a `Helpers.TextSynchronizer`,
- the object holds no missing script and no `Track.TrackCurveMeshBuilder`,
- no component other than the milepost itself listens to the marker's location changes,
- a `Track.TrackRelativePosition` on the object whose activity follows a track group activates this object itself, and no other component has replaced its activation callback,
- no loaded component other than the milepost and that `Track.TrackRelativePosition` holds a direct reference to the marker, and no new-game car placement uses it. If the patcher cannot read every field that could hold a marker, it refuses every marker until the next check,
- the graph holds one registration of the marker at its current location while the post is enabled and active, and none otherwise.

The inventory shows such an object in the `Milepost` family with the structure `milepost-v1`. For any other milepost, **Supported edits** on the **Object** tab reads **Not editable:** with the reason.

## Move, rotate or disable the milepost in the native map editor

1. Open **Scene patches** and select the milepost's row in the outliner, in the folder **Milepost**. **Select in world** also selects it, as described in [select world targets](select-world-targets.md).
2. Move or rotate the post on the **Placement** tab with **Numeric**, **Gizmo** or **Snap**, as described in [edit placement in the map editor](editing-placement-in-the-map-editor.md) and [use the transform gizmos](using-transform-gizmos.md). The binding stays as it is.
3. To hide the post, select **Disable** at the bottom of the **Object** tab. A milepost whose activity follows its track group stays off when the group is enabled later. **Enable** is refused for such a post; **Clear activity** returns it to its group, as described in [follow a track or endpoint](following-track.md#disable-an-object-whose-activity-follows-a-track-group).

## Set, replace or detach the binding in the native map editor

1. On the **Connections** tab, check **Current binding**: the stored segment, end and distance, and each graph registration under the marker ID with its owner.
2. Enter the target segment ID in **Segment**, the end (`a` or `b`) in **End** and the distance in metres in **Distance**, on the row **Track location (m)**. To take the segment from the world, select **Segment** in the **Replacement targets** section, click the track and choose **Confirm** in the footer: the **Segment** field takes the confirmed segment.
3. Select **Set**. **Detach** removes the post from the track, and **Clear binding** returns it to its underlying binding.

A missing or ambiguous segment, or a distance beyond the segment's length, is refused before anything changes. Native Undo and Redo reverse and repeat each edit. **Remove modification** returns the post to its underlying placement, activity and binding.

## Write the modification by hand

Copy the inspected identity with **Copy ID** in the command band and add a modification to the `modifications` object of `Data/scene-patches.json`, under a key of your choice, as described in [scene patch file v1](scene-patch-file-v1.md#milepost):

```json
"move-milepost-t46": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Mile Posts/T-46",
    "component": "Helpers.MilePost",
    "structure": "milepost-v1"
  },
  "placement": { "mode": "fixed", "position": [1025.0, 1.0, 2.0], "rotation": [0, 0, 0, 1] },
  "relationships": {
    "trackMarker": { "segment": "Stky0cg9c", "end": "b", "distance": 266.9192 }
  },
  "dependsOn": []
}
```

Omit `placement` or `relationships.trackMarker` to leave that setting to the scene. `{ "detach": true }` as `trackMarker` removes the binding. On every world load the patcher applies the placement and binding once; a repeated application keeps one registration and does not add movement.

## Limits

The automated tests check placement, binding, detach, explicit disable of a group-driven post, refusal of missing and ambiguous mileposts and replacement tracks, map mismatch, order-independent dependencies, repetition, the origin cache entry, preflight refusal, failure after a change with restoration or guarded retry, Undo, Redo, clear, removal, Save and reload against a controlled track graph. They do not load Railroader.

Research on Railroader 0.6.2 rebound the origin milepost T-46 from `Stvw` to T-47's location on `Stky0cg9c`, repeated that and restored it, with the T prefix cache populated. The cache kept the same T-46 object, T-47 kept T-46 as its origin, and the displayed `47` and both mile values stayed as they were. That trial did not test an entry that existed before it, other prefixes, a group change, reload or persistence.
