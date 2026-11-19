<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Repair the construction segments of a rail bridge

This guide shows how to set the construction segments of a rail bridge after you split or replaced its track in the native map editor. The construction segments are the track segments whose construction state a bridge's construction controller (`Game.Mow.TrackConstructionScenery`) reads to show or hide the parts of the bridge. The controller names them by ID. Construction segments require patcher 0.5.0 or later.

Three rail bridge constructions have a construction controller:

- `Bridge 1` at Bryson, with the controller on its group root.
- The `Almond Bridge`, with the controller on its group root.
- `Ela Bridge Scenery`, a controller on an object of its own that switches the steel and abutments of `Ela Bridge`.

A plate girder bridge has no construction controller and no construction segments.

## Why a repair is needed

The game code shows that splitting a segment in the native map editor removes the segment and adds two segments with new IDs, and that replacing a segment also removes its ID. The controller keeps naming the old ID. In Railroader, after a construction segment of `Bridge 1` at Bryson was deleted, the bridge's parts stayed visible and the game reported an error when the world loaded. What the game does after a split has not yet been observed.

The **Connections** tab lists each construction segment under **Observed bindings**, such as `Construction segment: <segment ID>`. A segment that no longer exists shows its ID followed by `no longer exists`. The construction then shows a problem on its **Object** tab under **Detected problems**, such as `Construction segment <segment ID> no longer exists`, and is marked **!** in the outliner. A construction segment without an ID is a problem too. Click **Repair construction segments** below the problem to open the **Connections** tab. The problem disappears after the repair. A part of `Bridge 1` or the `Almond Bridge` shows the remark on its **Connections** tab without a problem; repair the construction on its group root.

## Set the construction segments in the native map editor

1. Split or replace the bridge's track in the native map editor.
1. Open **Scene patches**, click **Refresh** in the command band and select the construction in the outliner: **Baked scenery group** > **Bridge** > **Rail bridges** > `Bridge 1`, `Almond Bridge` or `Ela Bridge Scenery`.
1. On the **Connections** tab, click **Segment** under **Replacement targets**, click the new track in the world and click **Confirm** in the footer.
1. Click **Picked** at the bottom of the tab. The segment ID is added to the **Segments** field. A segment that no longer exists leaves the field, and the other IDs stay.
1. Repeat the previous two steps for every segment the bridge stands on, such as both halves of a split segment. You can also type the IDs in the **Segments** field, separated by commas.
1. Click **Set**.
1. Click **Save** in the native map editor.

**Set** replaces the construction segments with exactly the segments in the field. The order of the IDs does not matter, and setting the same segments again changes nothing. The bridge does not move. **Set** is unavailable while the field is empty or holds the construction's current segments.

The patcher checks every segment before it changes anything. A segment that is missing from the loaded track, or whose ID more than one loaded segment carries, refuses the edit with that segment's ID. The patcher never picks another segment instead. The segments the construction has without this mod are accepted as they are, also when one of them no longer exists.

After the check, the patcher writes the segment IDs to the controller and lets the controller show the construction by the construction state of the new segments, as it does when the game enables it. Whether the construction then shows by the state of its new segments has not yet been observed in Railroader.

## Detach and clear

You cannot detach the construction segments: a construction without segments has no meaning. **Detach** stays unavailable, and a hand-written `{ "detach": true }` fails before anything changes: at world load, with **Use file version** and with **Import**. The construction keeps its segments. After **Use file version**, the footer names the refused modification with the reason, and the **Changes** tab reads `Refused.` with the reason. If you remove such a modification on the **Changes** tab, native **Undo** puts it back in the scene patch file without applying it, as it stood before.

**Clear segments** removes this mod's value: the construction returns to the segments it has without this mod. **Remove modification** on the **Changes** tab does the same and also removes this mod's placement and activity of the object. Native **Undo** returns the segments from before the edit, and **Redo** sets them again.

When the segments that **Clear segments**, **Remove modification** or **Undo** returns include one that no longer exists, such as the old ID of a split segment, the patcher writes them back but does not let the controller show the construction by them. The parts of the bridge keep the visibility of the repair until the world reloads.

## Ela Bridge Scenery

The construction segments are the only edit of `Ela Bridge Scenery`. Its placement stays unavailable because its position has no effect in the game: the controller switches the bridge's parts wherever it stands. **Enable** and **Disable** stay unavailable because the bridge's construction visibility runs on the controller. Move or disable `Ela Bridge` instead.

## Write the relationship by hand

Copy the identity with **Copy ID** in the command band and add `relationships.constructionSegments` to the construction's modification:

```json
"repair-bryson-bridge": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Bryson/Bridge 1",
    "component": "UnityEngine.Transform",
    "structure": "baked-scenery-group-v1"
  },
  "relationships": {
    "constructionSegments": {
      "segments": ["<first segment ID>", "<second segment ID>"]
    }
  },
  "dependsOn": []
}
```

For `Ela Bridge Scenery`, use `Game.Mow.TrackConstructionScenery` as the `component` and `bridge-construction-scenery-v1` as the `structure`. See [the scene patch file reference](scene-patch-file-v1.md#construction-segments) for every rule.

## If the edit fails

If the edit fails after the patcher wrote the segments, the patcher puts back the segments the controller named before the attempt. If it cannot confirm that, the footer reports that cleanup is incomplete and names the remaining construction segments. Further edits of the object are then blocked until you click **Recheck** or reload the native map editor.
