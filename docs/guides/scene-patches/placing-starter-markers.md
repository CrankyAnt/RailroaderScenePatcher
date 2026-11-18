<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Place a starter placement marker on the track

Use a starter placement marker to choose where a car cut appears in a new company game on your map. Each new-game car placement reads one marker's track location. The front of the cut starts at that track point. The rest extends towards the segment end from which the distance is measured. The marker object's position has no effect. To find these markers in the inventory, see [starter placement markers](inspect-scene-objects.md#starter-placement-markers).

You can change the track location, choose whether the cars start as a wreck, or do both. These edits require patcher 0.5.0 or later. They keep the map's choice of cars and loads, and leave the marker object's physical placement and activity unchanged.

## What the patcher checks first

At the start of a new company game, the setup places its car cuts in order. If one does not fit, the setup stops and none of the later cuts appear. Before changing a track location, the patcher checks the new location. It refuses the edit and explains why in any of these cases:

- The segment is missing from the loaded track, or more than one segment has its ID. The message names the segment. The patcher never substitutes another segment.
- The distance lies outside the segment. The message gives both the distance and the segment's length.
- The cut does not fit under the game's placement rule, taking the setup's other cuts into account. The check is described below. The message names the car placement, its cars and length, and the game's reason, such as `Not enough track.`, `Track not available.`, `Switch lined against.` or the earlier cut blocking it. The patcher also refuses a location that would block a later cut.
- No car placement in an active setup uses the marker, or more than one does. The patcher needs exactly one car placement to change.
- The edit would detach the marker's track location. The game needs that location to place the cut.
- Another scene marker has the same marker id. Removing a registration by marker id removes it for every marker with that id. The message names the duplicate id and all its holders. See [repair, replace or detach a track-marker binding](repairing-track-bindings.md).

The game's placement rule checks the front of the cut, then points every 1.5 metres towards the back, and finally the back end. The cut's length includes the gaps between cars and a steam locomotive's tender. If the length is an exact multiple of 1.5 metres, the game checks the last step twice instead of checking the back end. The patcher follows the same rule.

Every checked point must be on available track, with no car from an earlier cut within 0.75 metres. The track must continue to the back of the cut, with no switch lined against it.

The check uses the other cuts' locations from the same scene patch file, including when that file loads. If another starter placement moves a cut, the check uses that cut's new location. This allows two cuts to swap places. In the native map editor, first move one cut out of the way, then move the other into its old place.

An unrelated failure in another placement, such as a marker on a missing segment, does not block this edit. Fix that marker separately.

The check uses the track groups and switch positions currently set in the native map editor. A new company game starts from that same map data.

## Place the marker in the native map editor

1. Open **Scene patches**, click **Refresh** in the command band and select the marker under **Starter placement**. A gold point marks the front of its cut. A gold ribbon extends back along the track to show where the cut stands and which way it extends.
2. Open the **Connections** tab. Under **Place on the track**, click **Segment** and click where the front of the cut should stand. Check the candidate in the footer and click **Confirm**. This fills the **Track location (m)** fields with the segment, end `a` and the distance of the click from end `a`.
3. To reverse the cut's direction, change **End** to `b` and adjust **Distance** as needed. The distance now counts from end `b`. You can also enter all three fields without clicking the track.
4. Click **Set** at the bottom of the tab.

The patcher runs the checks above. If it refuses the edit, the footer explains why and the marker keeps its track location. If the edit succeeds, the patcher registers the marker at the new track location and moves the gold point and ribbon there. The **Changes** tab shows the old and new locations. The edit takes one native Undo step.

**Detach** stays unavailable because a starter placement marker must have a track location. The modes on the **Placement** tab and the activity buttons on the **Object** tab are also unavailable. The marker object's placement and activity do not affect where the cars start.

## Choose whether the cars start as a wreck

The wreck setting determines whether the cars start derailed or on the track. Derailed cars use the game's own wreck behavior.

1. Inspect the marker as in step 1 above and open the **Connections** tab.
2. Under **Starting cars**, the **Start derailed** checkbox shows the cut's current setting. This is the map's setting unless this mod's scene patch file overrides it.
3. Select the checkbox to make the cut a wreck, or clear it to place the cut on the track.

The footer reports the result. The **Changes** tab shows the old and new settings, such as `Start derailed: from No to Yes`, and the **Used by** lines show the updated setting. The edit takes one native Undo step.

The checkbox is unavailable if the patcher cannot read the new-game setups or identify exactly one new-game car placement in an active setup that uses this marker. The tooltip explains why, prefixed with `Unavailable:`. The patcher also checks this before changing anything and refuses an edit for the same reason.

Changing the wreck setting keeps the marker's track location. Moving the marker to another track location keeps the wreck setting.

## Undo, clear and remove

- Native **Undo** restores the previous track location and registration, or the previous wreck setting. **Redo** reapplies the edit. The title, unsaved-change state and **Save** button update accordingly.
- **Clear location**, at the bottom of **Connections**, removes this mod's track location and restores the location from the loaded map. It keeps any wreck setting in the same modification. If nothing else remains, the patcher removes the modification from the scene patch file.
- Returning the **Start derailed** checkbox to the setting the cut has without this mod removes this mod's wreck setting. It keeps any track location in the same modification. If nothing else remains, the patcher removes the modification from the file.
- **Remove modification** in the footer removes the entire modification. It restores the track location and wreck setting the marker and car placement have without this mod, recalculated from the loaded map and other mods' values.
- Returning the marker to the map's track location removes that setting. If nothing else remains, the patcher removes the modification from the file.

If an edit fails after changes begin, the patcher restores the previous wreck setting, track location and registration. If it cannot verify restoration, it reports incomplete cleanup and blocks further edits to the marker until **Recheck** in the footer succeeds or the world reloads. **Recheck** cannot succeed while the graph registration differs from the stored track location.

## Save and play

Click **Save** in the native map editor to write the modification to `Data/scene-patches.json`. Save raises the mod's patcher dependency to 0.5.0, preserving any stricter constraint already in `Info.json`. The scene patch file remains in format version 1.

When a player starts a new company game, the patcher applies the scene patch file during world load, before the setup places its cars. The cut then starts at the patched track location, derailed if the file sets it as a wreck. The same checks run at load. If a location fails, the scene patch file does not apply, the marker keeps the map's location and a patcher warning explains why. Loading an existing save does not run the setup, so these settings place no cars in that case.

## Write the starter placement by hand

Click **Copy ID** in the command band to copy the identity. Add a modification under a key of your choice, as described in [scene patch file v1](scene-patch-file-v1.md#starter-placement):

```json
"starter-ewh-int2": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "Setups/Starter Equipment Setup/starter-ewh-int2",
    "component": "Track.TrackMarker",
    "structure": "starter-placement-v1"
  },
  "starterPlacement": {
    "setup": "ewh-steam",
    "track": { "segment": "Stky030z8", "end": "b", "distance": 45 },
    "wreck": false
  },
  "dependsOn": []
}
```

For `setup`, use the setup identifier shown under **Read by** on the **Object** tab. Its car placement uses this marker. Omit `track` to keep the map's track location. Omit `wreck` to keep the map's wreck setting, use `true` to make the cut a wreck, or use `false` to place it on the track.

## Limits

Automated tests use a controlled setup and track. They cover explicit and omitted track locations, refusal to detach, missing and ambiguous segments, distances outside a segment, and cuts that run out of track, reach unavailable track, cross a switch lined against them or collide with another cut. They also cover markers used by no car placement or by several, repeated application, failure restoration, guarded retry, Undo, Redo, removal and canonical Save. The wreck tests cover omitted, on and off settings and the same refusal when no single car placement uses the marker. These tests do not load Railroader.

When checking earlier cuts, the patcher treats each cut as a continuous body along the track, extending half a car's width to either side. The game checks individual cars. As a result, the patcher also treats the gaps between an earlier cut's cars as blocked.

Only a new company game in Railroader can show whether the patched track location and wreck setting take effect, and where and how the cars appear. That evidence applies to the tested build, setup and cut. Multiplayer is not covered.
