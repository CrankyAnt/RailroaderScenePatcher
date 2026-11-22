<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Reposition or rebind a milepost

Move, rotate, or disable a milepost, or set, replace, or detach its track binding. A milepost displays a mile value beside the track. Its scene object holds these components:

- `Helpers.MilePost`, with a prefix, an origin flag, and a mile value.
- `Track.TrackMarker`, with the track binding.
- `Helpers.TextSynchronizer`, with the displayed text.

Physical placement and track binding are separate settings. Moving the post leaves its binding unchanged; changing the binding does not move the post. The post's track marker stores the binding as a segment ID, the end of the segment from which the distance is measured, and the distance in metres. The track graph registers the marker under that segment.

Edits don't change the milepost's model, mile value, prefix, origin flag, or displayed text. The patcher never recalculates a mile value. The first time the game looks up a prefix's origin milepost, it caches the post itself. Editing the post's location leaves that cache unchanged. A scene patch file with a milepost modification needs patcher version 0.5.0 or later. The native map editor's Save writes that dependency.

## What qualifies

To edit a milepost, all of these conditions must hold:

- The object holds exactly one `Helpers.MilePost`, exactly one `Track.TrackMarker` of type `Generic` with a non-empty marker ID, and a `Helpers.TextSynchronizer`.
- The object has no missing script and no `Track.TrackCurveMeshBuilder`.
- No component other than the milepost itself listens to the marker's location changes.
- If a `Track.TrackRelativePosition` on the object makes its activity follow a track group, it activates this object itself. No other component has replaced its activation callback.
- No loaded component other than the milepost and that `Track.TrackRelativePosition` holds a direct reference to the marker, and no new-game car placement uses it. If the patcher cannot read every field that could hold a marker, it refuses every marker until the next check.
- While the post is enabled and active, the marker has exactly one named location and it matches the stored track location. Otherwise the marker has no named location.

The inventory lists an object that meets these conditions in the `Milepost` family with the structure `milepost-v1`. For any other milepost, **Supported edits** on the **Object** tab reads **Not editable:** and gives the reason.

## Move, rotate, or disable the milepost in the native map editor

1. Open **Scene Patcher** and select the milepost in the outliner's **Milepost** folder. You can also use **Select in world**, as described in [select world targets](select-world-targets.md).
2. On the **Placement** tab, move or rotate the post with **Numeric**, **Gizmo**, or **Snap**. See [edit placement in the map editor](editing-placement-in-the-map-editor.md) and [use the transform gizmos](using-transform-gizmos.md). The binding stays unchanged. **Follow** is inactive because a milepost uses fixed placement.
3. To hide the post, click **Disable** at the bottom of the **Object** tab. If the post's activity follows its track group, it stays off when the group is enabled later. **Enable** is unavailable for these posts. Its tooltip explains why. **Clear activity** returns it to its group, as described in [follow a track or endpoint](following-track.md#disable-an-object-whose-activity-follows-a-track-group).

## Set, replace, or detach the binding in the native map editor

1. On the **Connections** tab, check **Current binding**. It shows the stored segment, end, and distance, and one **Named location** row per track location registered under the marker ID. The row says when the location belongs to another object.
2. In the **Track location (m)** row, enter the target segment ID in **Segment**, the end (`a` or `b`) in **End**, and the distance in metres in **Distance**. To pick the segment in the world, click **Segment** under **Replacement targets**, click the track, and click **Confirm** in the footer. The **Segment** field takes the confirmed segment.
3. Click **Set**. **Detach** removes the post from the track, and **Clear binding** returns it to its underlying binding.

Before changing anything, the patcher refuses the edit if the segment is missing or ambiguous, or the distance exceeds its length. Native Undo and Redo reverse and repeat each edit. **Remove modification** returns the post to its underlying placement, activity, and binding.

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

Omit `placement` or `relationships.trackMarker` to leave that setting to the scene. Set `trackMarker` to `{ "detach": true }` to remove the binding. The patcher applies the placement and binding once on every world load. Repeated application keeps one registration and does not move the post again.

## Limits

The automated tests use a controlled track graph; they do not load Railroader. They check placement, binding, detach, explicit disable of a group-driven post, and refusal of missing and ambiguous mileposts and replacement tracks. They also check a scene target map that is not the loaded map, order-independent dependencies, repetition, the origin cache entry, preflight refusal, failure after a change with restoration or guarded retry, Undo, Redo, clear, removal, Save, and reload.

The patcher does not repair a milepost's binding before the game checks its track markers at world load. A broken milepost binding therefore still raises the game's "not on the track" warning once in that load, before the patcher applies the scene patch file and repairs it. The early repair described under [repair feedback](repairing-track-bindings.md#repair-feedback-in-the-native-map-editor) covers Generic track markers only.

In research on Railroader 0.6.2, T-46 was rebound from `Stvw` to T-47's location on `Stky0cg9c`, rebound again and restored, with the T prefix cache populated. T-46 was the origin milepost. The cache kept the same T-46 object, T-47 kept T-46 as its origin, and the displayed `47` and both mile values stayed unchanged. The trial did not test an entry that existed before it, other prefixes, a group change, reload, or persistence.
