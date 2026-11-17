<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Repair, replace or detach a track-marker binding

This guide shows how to set, replace or detach the track location of a Generic track marker, such as a yard-limit or station sign, and what changes with it. The binding is the location stored in the object's `Track.TrackMarker`: a segment ID, the segment end the distance counts from, and the distance in metres. The track graph registers the marker under that segment, and game code that looks the marker up by its ID finds it there.

A binding edit never moves the object. Placement and activity are separate settings of the same modification; you can change them before, after or without a binding edit. Track-marker relationship and target capabilities require patcher 0.5.0 or later. The native map editor's Save derives the dependency minimum from the scene patch file's content; see [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## What qualifies

The patcher edits a marker when all of these hold:

- the object holds exactly one `Track.TrackMarker`, of type `Generic`, with a non-empty marker ID,
- the object holds no `Helpers.MilePost` and no `Track.TrackCurveMeshBuilder`,
- no other component listens to the marker's location changes,
- a `Track.TrackRelativePosition` on the object whose activity follows a track group activates this object itself, and no other component has replaced its activation callback,
- no loaded component other than that `Track.TrackRelativePosition` holds a direct reference to the marker, and no new-game car placement uses it. If the patcher cannot read every field that could hold a marker, it does not know the holders and refuses every marker until the next check,
- the graph holds one registration of the marker at its current location while the marker is enabled and active, and none otherwise. A registration that another scene marker with the same marker id holds is not a registration of this marker.

A Generic marker inside a grade-crossing setup, such as a whistle sign under a `Track.Crossings.GradeCrossingController`, qualifies like any other: the crossing controller reads its circuits and spans, and the auto engineer's crossing horn reads the crossing pad's `Crossing` marker, not the sign. The crossing pad and the crossing's own `Crossing` markers keep their own operations.

Each of these refusals names a consumer of the marker's location that a binding edit would leave behind. For an object whose activity follows its track group, a binding edit also moves that group subscription to the new segment's group; see [follow a track or endpoint](following-track.md#disable-an-object-whose-activity-follows-a-track-group).

The inventory lists an object that meets these conditions in the `Track marker` family with the structure `track-marker-v1`. For any other marker, **Supported edits** on the **Object** tab reads **Not editable:** with the reason. A marker of another type than Generic stands in the **Inspection only** group of the outliner instead, and a starter placement marker stands in its own **Starter placement** family, described in [inspect scene objects](inspect-scene-objects.md#starter-placement-markers). Crossing pads have their own operation, described in [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md). Mileposts have their own operation, described in [reposition or rebind a milepost](repositioning-mileposts.md); the detection coverage of a crossing is corrected as described in [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md). Signal markers fall outside the supported operations.

## Set or replace the binding in the native map editor

1. Open **Scene patches**, select **Refresh** in the command band, and select the marker's row in the outliner. **Select in world** also selects it, as described in [select world targets](select-world-targets.md).
2. On the **Connections** tab, **Replacement targets** and the fields of the binding come first, and **Current binding** below them shows what the marker is bound to now; the world marks that track in green while the tab is open. Check the **Current binding** lines. They show the stored segment, end and distance, and each graph registration under the marker ID with its owner. For an enabled marker, a stored segment that no longer exists appears under **Detected problems** on the **Object** tab as a broken binding; for a disabled one, and for an empty binding, a **Marker binding note** says what the state means.
3. Enter the target segment ID in **Segment**, the end (`a` or `b`; it reads in lower case everywhere, and its case does not matter when you type it) in **End** and the distance in metres in **Distance**, on the row **Track location (m)**. The segment ID is taken as you write it: the game tells `S377` from `s377`. To take the segment from the world, select **Segment** in the **Replacement targets** section, click the track and choose **Confirm** in the footer: the fields take the segment, end and distance of the click. **Picked** fills the segment again after you changed that field.
4. Select **Set** at the bottom of the tab.

The patcher checks the target before it changes anything: the segment must exist in the loaded track graph and contain the distance. A missing segment, a distance beyond the segment or an end other than `a` or `b` refuses the edit, and the marker keeps its binding and registration. The patcher never picks a nearby segment instead.

After the check it removes the marker's graph registration, writes the new location, and registers the marker at that location when the marker is enabled and active. A map label below the marker is registered with the map again, so its dimming follows the track group of the new location. The patcher then verifies that the stored location is the mod's location, that the graph holds exactly one registration, for this marker, at that location, and that each registered map label is dimmed by the new location's track group. Any difference fails the edit and restores the earlier location and registration.

## Detach the marker

Select **Detach** at the bottom of the tab. The stored location becomes empty and the graph holds no registration for the marker, so a lookup by its ID no longer finds it. The object stays where it is and keeps its activity. The scene patch file records an explicit `"detach": true`; leaving the relationship out of the scene patch file would leave the binding untouched.

## Undo, clear and remove

Native Undo and Redo reverse and reapply each binding edit together with its registration. **Clear binding** at the bottom of the **Connections** tab removes only the relationship from this mod's modification: the marker returns to the binding the loaded map gives it, while the mod's placement or activity stays. **Remove modification** in the footer removes the whole modification and returns binding, registration, placement and activity to the loaded map's values. Other objects' modifications stay applied.

If an edit fails and the patcher cannot verify the restoration, it reports incomplete cleanup and refuses further edits of the marker until **Recheck** in the footer succeeds or the world reloads. Recheck refuses while the graph registration does not match the stored location.

## Activity and registration

A disabled marker keeps its binding without a registration. When it is enabled, the game registers it at the bound location. An explicit `"active": false` in the same modification removes the registration and keeps the binding.

## Markers with a duplicate marker id

Some scene markers hold a duplicate marker id: another scene marker holds the same marker id. In the base-game map, the active speed sign `Speed - Fontana South Curve (3)` and an inactive yard limit hold `Mg6m`, and six `Whistle Sign W` markers hold `Mkft`. The game removes a marker's graph registration by its marker id, and it removes that marker id from every segment. Disabling or rebinding one holder would therefore also remove the registration of the others.

The **Dependencies** section on the **Object** tab of such a marker starts with the duplicate marker id, the number of scene markers that hold it and why their edits are refused. One sentence per holder follows: first the inspected marker, as **This marker**, then the active holders and the inactive ones last, each with its scene and path, whether it is active, and where it has a registration under the marker id, or that it has none. A registration under the marker id that no scene marker holds gets a sentence of its own. A scene marker that is not loaded but still owns a registration under the marker id also counts as a holder.

Until the duplicate marker id is repaired, the patcher refuses these edits of every holder, a Generic marker or a crossing pad:

- an activity change, such as **Enable** or **Disable**,
- a binding change: a new location or a detach,
- track following.

The refusal stands in the message area of the footer, under the action group it came from, such as **Activity**: first the refused action, such as `Enable refused.`, then the duplicate marker id and every scene marker that holds it, with each one's activity, the edited marker first and inactive holders last. The patcher changes nothing: the scene, the scene patch file and every holder's registration stay as they were. Reloading the map does not help, because it registers the same markers under the same marker id again. Choosing another track does not help either, because the edit still removes the registrations under that marker id. A fixed placement of a holder stays editable, since it does not touch the registration, unless a map label below the marker caches another track group than its location: correcting that cache would register the marker again, so that edit is refused too. A value equal to the marker's current one, such as `"active": false` on an inactive marker, changes nothing and is accepted.

## Write the binding by hand

Copy the identity with **Copy ID** in the command band and add a modification to the `modifications` object of `Data/scene-patches.json`, under a key of your choice, as described in [scene patch file v1](scene-patch-file-v1.md):

```json
"bind-station-1": {
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

## Repair feedback in the native map editor

When a marker binding edit is refused, read the scene patch editing feedback beside the binding controls. It names the failed operation and reason. Refresh the inventory and inspect the marker again before retrying, especially after Undo, Redo or another native edit. Scene patch editing keeps unknown or changed definitions protected; it does not ask players to edit JSON.

Some markers can emit a game warning before the patcher can apply a mod repair. The patcher attempts an early repair only for a dependency-free relationship-only operation, before the game's original `TrackMarker.OnEnable` check, when the target belongs to a valid scene and the track graph is populated. The game's check still runs. If that route is unavailable, the patcher can remove only the exact current warning row emitted by that check, and only after the applicable operation succeeds and the marker binding and dependent state pass a separate verification. Historical log entries are not changed. Unknown, changed, ambiguous or unrelated warnings remain. If the patcher cannot establish ownership of a warning window, it leaves the window open; you may close it manually. The patcher does not close player windows.

Recover unknown content or scene patch file format problems in the native map editor. Players should not create or edit JSON to resolve these reports.

Remove a repair with **Remove modification** in the footer. This restores the underlying reference even when its original track no longer exists. For an active marker the game's check reports that broken reference again. Undo restores the repaired binding and registration and reconciles only the unchanged warning emitted for that restoration; Redo restores the broken binding and its warning. Unrelated or subsequently changed warnings remain. If several modifications in the same scene patch file target the object, combine them into one before editing or removing it in the native map editor.

## Limits

The automated tests check set, replacement, detachment, omission, refusal of missing, ambiguous and out-of-range targets, dependency isolation, failure restoration, guarded retry, Undo, Redo, removal and reload against a controlled track graph. They also check the dependency lines and refusals for a pair and a group of three holders of a duplicate marker id, a holder whose activity follows its track group, crossing pads, and a holder that is not loaded, and that no holder loses its registration. They do not load Railroader. One native journey on Railroader with patcher commit `c4b6cb9` inspected the `Mg6m` pair and the six `Mkft` holders, one of them inactive through an inactive parent, and saw Enable and a rebind of the yard limit and a Disable of a whistle sign refused while every holder's registration stayed the same. That journey used the earlier wording of the explanation.

The supported pattern comes from the game's source: setting a marker's location does not change its graph registration, so the patcher repeats the registration steps the marker performs when it is enabled and disabled.

One native journey on Railroader 0.6.2 with patcher source `a59b8d9` exercised the yard limit Mkwd in the native map editor of a test mod, with four other inherited signs as unchanged controls. Set, replacement, detachment, refusal of a missing segment and of a distance beyond the segment, a placement change, native Undo and Redo, clearing the mod's binding, physical snap, Save, native map editor reload and removal behaved as described above. Each bound state had exactly one graph registration owned by the marker and a matching graph lookup, a detached marker had none, and the controls kept their bindings, registrations and placements. That journey did not cover a completed transform-gizmo placement of a bound marker, which it refused as a changed target, a map-label refresh, because no admitted sign has a registered map label, or a second application within one load. It covers that marker, build and fixture only.

The game reads a Generic marker's binding in these places:

- Track scripts look a marker up by ID in the graph registration, and fall back to the marker's stored location.
- The map dims a label below a marker by the track group of the marker's location, cached when the label registers. The patcher refreshes that cache.
- A new-game car placement reads its marker's location when the setup runs. Such a marker is a starter placement marker with its own family; see [starter placement markers](inspect-scene-objects.md#starter-placement-markers).
- The flare and planner queries read registrations of their own marker types, not of Generic markers.
- `Track.TrackRelativePosition` moves its object only when its relative placement is configured again. In the game, map objects do that, not the inherited scene signs.

The inherited Generic signs in the research inventory are the whistle signs M5pp near Sylva and Mm8w at `World/Large Scenery/Ela/Crossing Unprotected - Bryson Everett/Whistle Sign E`, the speed sign Mwp1 at `World/Large Scenery/Alarka Signs/Speed Sign`, the yard limit Mkwd at `World/Large Scenery/Alarka Signs/Yard Limit` and the station sign Mi4f at `World/Large Scenery/Ela/Signs/Station Sign (1)`. In that journey's inventory, M5pp, Mm8w, Mkwd and Mi4f carried `track-marker-v1`; Mwp1's activity follows its track group, so that patcher version refused it. Patcher 0.5.0 admits it with its group subscription. Only Mkwd was edited; the other admitted signs are unverified after a binding edit, a Save and a reload. Segment IDs are unique keys of the loaded track graph, so the game itself has no ambiguous segment target. The ambiguity refusal guards the scene patch file contract.

A track-relative object does not move when its marker is rebound, and its terrain snap does not run. For an object whose activity follows its track group, rebinding moves the subscription and keeps an explicit disable in place. Multiplayer is not covered.
