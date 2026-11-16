<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Place a starter placement marker on the track

This guide shows how to choose where a new company game on your map places a cut of starting cars. Each car placement of a new-game setup reads one starter placement marker for its track location: the front of the cut stands at that point, and the cut extends from there towards the end of the segment that the distance counts from. The marker's object position plays no part. See [starter placement markers](inspect-scene-objects.md#starter-placement-markers) for how the inventory lists these markers.

A starter placement changes only the marker's track location. The cars of the cut, their loads, the wreck setting and the marker object's placement and activity stay as the map has them. A starter placement requires patcher 0.5.0 or later.

## What the patcher checks first

When the game starts a new company game, the setup places its cuts one after the other. When a cut does not fit, the game stops the whole setup, and the cuts after it never appear. So the patcher checks a new track location before it changes anything, and refuses it with the reason when:

- the segment does not exist on the loaded track, or the track holds more than one segment with that ID. The refusal names the segment, and no other segment is used instead;
- the distance lies outside the segment. The refusal gives the distance and the segment's length;
- the whole cut, together with the cuts of the setup's other placements, fails the game's own placement rule. The game checks the front and a point every 1.5 metres back over the length of the cut, the cars with the gaps between them and the tender of a steam locomotive included, and its back end; for a cut whose length is a whole number of 1.5-metre steps the game checks its last step a second time instead of the back end, and the patcher does the same. Each of these points must be on track that is available, the track must not end and no switch may be lined against the cut before the back end, and no car of a cut that the setup places earlier may stand within 0.75 metres. The refusal names the car placement, its cars and length, and the game's reason, such as `Not enough track.`, `Track not available.`, `Switch lined against.` or the earlier cut it runs into. The new location is also refused when a cut that the setup places later would run into this one;
- no car placement of an active setup reads the marker, or more than one does. A starter placement changes the one car placement that reads its marker;
- the marker's track location would be detached. The game reads it to place the cut.
- another scene marker holds the same marker id. The game removes a marker's registration by marker id from every marker that holds it, so the refusal names the duplicate marker id and every holder; see [repair, replace or detach a track-marker binding](repairing-track-bindings.md).

The other cuts stand where your patch document places them: a cut that another starter placement of the same document moves is judged at its new track location, also when the game loads the document, so two cuts can swap places. Move one of them away first in the native map editor, then the other into its old place. A failure of another placement that this cut does not cause, such as another marker on a missing segment, does not refuse this location. Fix that marker on its own.

The patcher checks against the track as the native map editor has it: which track groups are available and which way each switch is lined. A new company game starts from the same map data.

## Place the marker in the native map editor

1. Open **Scene patches**, select **Refresh** in the command band and select the marker's row in the **Starter placement** family. The world marks the marker's current track point in gold and the track its cut stands on as a gold ribbon from that point back, so you can see where the cut starts and which way it extends.
2. Open the **Connections** tab. Under **Place on the track**, select **Segment**, click the track where the front of the cut belongs, check the candidate in the footer and choose **Confirm**. The **Track location (m)** fields take the confirmed segment, end `a` and the distance of the click from end `a`.
3. Change **End** to `b` when the cut should extend the other way along the segment, and adjust **Distance** if needed: with end `b` the distance counts from end `b`. You can also type all three fields without clicking the track.
4. Select **Set** at the bottom of the tab.

The patcher runs the checks above. A refusal appears in the footer with its reason, and the marker keeps its track location. After a successful edit the marker is registered at the new location, the gold point and ribbon move there, and the **Changes** tab shows the old and the new track location. The edit is one native Undo step.

**Detach** at the bottom of the tab stays greyed out: a starter placement marker cannot be detached. The **Placement** tab's modes and the activity buttons on the **Object** tab stay greyed out too, because the object's placement and activity do not change where the cars start.

## Undo, clear and remove

- Native **Undo** returns the track location from before the edit, with its registration; **Redo** applies the edit again. The title, the dirty state and the **Save** button follow.
- **Clear location** at the bottom of the **Connections** tab removes this mod's track location: the marker returns to the track location the loaded map gives it. A modification that sets nothing else leaves the document.
- **Remove modification** in the footer removes the whole modification and returns the marker to the loaded map's track location, computed again from the map and the other mods' values.
- Setting the marker back to the map's own track location reverts the setting, and the modification leaves the document.

If an edit fails after the patcher started to change the marker, it restores the track location and registration the marker had before. When it cannot verify that restoration, it says so and refuses further edits of the marker until **Recheck** in the footer succeeds or the world reloads. **Recheck** refuses while the graph registration does not match the stored location.

## Save and play

The native map editor's **Save** writes the modification to `Data/scene-patches.json` and raises the mod's dependency on the patcher to 0.5.0, keeping a stricter constraint that `Info.json` already has. The document stays in format version 1.

When a player starts a new company game on the map, the patcher applies the document when the world loads, before the setup places its cars, so the cut stands at the patched track location. The same checks run then: a document whose location fails them is not applied, the marker keeps the map's track location and the player sees a patcher warning that names the reason. In a game loaded from a save the setup does not run, so the patched track location places no cars there.

## Write the starter placement by hand

Copy the identity with **Copy ID** in the command band and add a modification under a key of your choice, as described in [patch document v1](patch-document-v1.md#starter-placement):

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
    "track": { "segment": "Stky030z8", "end": "b", "distance": 45 }
  },
  "dependsOn": []
}
```

`setup` names the setup whose car placement reads the marker, as the **Read by** lines on the **Object** tab show it.

## Limits

The automated tests check set and omitted track locations, the detach refusal, missing and ambiguous segments, a distance outside the segment, a cut that runs off the track, onto unavailable track, across a switch lined against it or into another cut, a marker that no car placement or several read, repeated application, failure restoration, guarded retry, Undo, Redo, removal and the canonical Save, against a controlled setup and track. They do not load Railroader.

The check of earlier cuts treats each cut as one body along its track from its front to its back, half a car wide on each side; the game checks the cars themselves, so a point in the gap between two cars of an earlier cut counts as blocked here. Whether a patched location reaches a new company game, and where its cars then stand, is shown only by a Railroader journey for a tested build, setup and cut. Multiplayer is not covered.
