<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Repair the construction segments of a rail bridge

Use this guide to set a rail bridge's construction segments after splitting or replacing its track in the native map editor. The bridge's construction controller (`Game.Mow.TrackConstructionScenery`) uses these segments' construction state to show or hide parts of the bridge. It identifies the segments by ID. Construction segments require patcher 0.5.0 or later.

You can set construction segments for three rail bridge constructions:

- `Bridge 1` at Bryson, with the controller on its group root.
- The `Almond Bridge`, with the controller on its group root.
- `Ela Bridge Scenery`, a controller on an object of its own that switches the steel and abutments of `Ela Bridge`.

A plate girder bridge has no construction controller and no construction segments.

## Why a repair is needed

The game code shows that splitting a segment in the native map editor removes it and adds two segments with new IDs. It also shows that replacing a segment removes its ID. The controller still refers to the old ID. In Railroader, deleting a construction segment of `Bridge 1` at Bryson left the bridge's parts visible and caused an error when the world loaded. After a split, the game reported `Error during DidRestore of Bridge 1 (Game.Mow.TrackConstructionScenery)` at every world load until the repair.

The game normally runs the controller's update before scene patch files apply. If a native segment is missing and the patcher finds a replacement set on the loaded track, it can defer the update. It checks settings in load order and uses the last one that passes the segment check. A setting that restores the native set also passes that check and can leave the native error in place. The game runs a deferred update on the next frame after the patcher applies the files. In Railroader 0.6.3, repairing Bryson Bridge 1 eliminated its `DidRestore` error in a new game, a loaded save, and the editor. A valid-looking repair blocked by `dependsOn` still produced the native error after the patcher reported the failed load. A repair with a missing replacement segment produced the native error before the patcher refused it.

The **Connections** tab lists each construction segment under **Observed bindings**, such as `Construction segment: <segment ID>`. A segment that no longer exists shows its ID followed by `no longer exists`. The construction then shows a problem on its **Object** tab under **Detected problems**, such as `Construction segment <segment ID> no longer exists`, and is marked **!** in the outliner. A construction segment without an ID is a problem too. Click **Repair construction segments** below the problem to open the **Connections** tab. The problem disappears after the repair. A part of `Bridge 1` or the `Almond Bridge` shows the remark on its **Connections** tab without a problem; repair the construction on its group root.

## Set the construction segments in the native map editor

1. Split or replace the bridge's track in the native map editor.
1. Open **Scene Patcher**, click **Refresh** in the command band, and select the construction in the outliner: **Baked scenery group** > **Bridge** > **Rail bridges** > `Bridge 1`, `Almond Bridge`, or `Ela Bridge Scenery`.
1. On the **Connections** tab, click **Segment** under **Replacement targets**, click the new track in the world, and click **Confirm** in the footer.
1. Click **Picked** at the bottom of the tab. The segment ID is added to the **Segments** field. A segment that no longer exists leaves the field, and the other IDs stay.
1. Repeat the previous two steps for every segment the bridge stands on, such as both halves of a split segment. You can also type the IDs in the **Segments** field, separated by commas.
1. Click **Set**.
1. Click **Save** in the native map editor.

**Set** replaces the construction segments with exactly those in the field. The IDs can be in any order. Setting the same segments again changes nothing. The bridge doesn't move. **Set** is unavailable while the field is empty or holds the construction's current segments.

The patcher checks every segment before changing anything. It refuses the edit if a segment is missing from the loaded track or more than one loaded segment has its ID. The refusal names that ID. The patcher never substitutes another segment. It accepts the construction's segments without this mod as they are, even if one no longer exists.

After the check, the patcher writes the segment IDs to the controller. If the controller is enabled and the segments exist, it updates the construction visibility. In Railroader 0.6.3, changing a replacement segment of Bryson Bridge 1 from Complete to Not Started hid the relevant bridge parts. Restoring Complete showed them again.

## Detach and clear

You can't detach construction segments because a construction without segments has no meaning. **Detach** stays unavailable. A hand-written `{ "detach": true }` fails before anything changes, whether at world load, with **Use file version**, or with **Import**. The construction keeps its segments. After **Use file version**, the footer names the refused modification and gives the reason. The **Changes** tab shows `Refused.` and the reason. If you remove that modification on the **Changes** tab, native **Undo** restores it to the scene patch file without applying it, just as it was before removal.

**Clear segments** removes this mod's value: the construction returns to the segments it has without this mod. **Remove modification** on the **Changes** tab does the same and also removes this mod's placement and activity of the object. Native **Undo** returns the segments from before the edit, and **Redo** sets them again.

If **Clear segments**, **Remove modification**, or **Undo** restores a segment that no longer exists, such as the old ID of a split segment, the patcher writes the segments back but prevents the controller from using them to set construction visibility. The bridge's parts keep the visibility set by the repair until the world reloads.

## Ela Bridge Scenery

You can edit only the construction segments of `Ela Bridge Scenery`. Placement is unavailable because the controller's position has no effect in the game. It switches the bridge's parts wherever it stands. **Enable** and **Disable** are unavailable because the controller manages the bridge's construction visibility. Move or disable `Ela Bridge` instead.

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

If an edit fails after writing the segments, the patcher restores the controller's segments from before the attempt. If it can't confirm the restoration, the footer reports incomplete cleanup and names the remaining construction segments. Further edits of the object stay blocked until you click **Recheck** or reload the native map editor.
