<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Repair, replace, or detach a track-marker binding

Set, replace, or detach the track location of a Generic track marker, such as a yard-limit or station sign. Its `Track.TrackMarker` stores the binding as a segment ID, an end to measure from, and a distance in meters. The track graph registers the marker under that segment so game code can find it by ID.

Changing the binding never moves the object. Placement and activity are separate settings in the same modification. You can change them before or after a binding edit, or without editing the binding at all. Track-marker relationship and target capabilities require patcher 0.5.0 or later. The native map editor's Save derives the dependency minimum from the scene patch file's content. See [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## What qualifies

The patcher can edit a marker only when all these conditions hold:

- the object holds exactly one `Track.TrackMarker`, of type `Generic`, with a non-empty marker ID,
- the object holds no `Helpers.MilePost` and no `Track.TrackCurveMeshBuilder`,
- no other component listens to the marker's location changes,
- a `Track.TrackRelativePosition` on the object whose activity follows a track group activates this object itself, and no other component has replaced its activation callback,
- no loaded component other than that `Track.TrackRelativePosition` holds a direct reference to the marker, and no new-game car placement uses it. If the patcher cannot read every field that could hold a marker, it does not know the holders and refuses every marker until the next check,
- while the marker is enabled and active, it has exactly one named location and it matches the stored track location. Otherwise the marker has no named location. A named location for another scene marker with the same marker ID does not count for this marker.

The same conditions apply to a Generic marker inside a grade-crossing setup, such as a whistle sign under a `Track.Crossings.GradeCrossingController`. The crossing controller reads its circuits and spans. The auto engineer uses the crossing pad's `Crossing` marker to sound the horn, so it doesn't read the sign. The crossing pad and the crossing's own `Crossing` markers keep their own operations.

When a binding edit would leave a consumer of the marker's location unchanged, the refusal names that consumer. If the object's activity follows its track group, changing the binding also moves its group subscription to the new segment's group. See [follow a track or endpoint](following-track.md#disable-an-object-whose-activity-follows-a-track-group).

The inventory lists an object that meets these conditions in the `Track marker` family with the structure `track-marker-v1`. For any other marker, **Supported edits** on the **Object** tab reads **Not editable:** with the reason. A marker of another type than Generic stands in the **Inspection only** group of the outliner instead, and a starter placement marker stands in its own **Starter placement** family, described in [inspect scene objects](inspect-scene-objects.md#starter-placement-markers). Crossing pads have their own operation, described in [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md). Mileposts have their own operation, described in [reposition or rebind a milepost](repositioning-mileposts.md); the detection coverage of a crossing is corrected as described in [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md). Signal markers fall outside the supported operations.

## Set or replace the binding in the native map editor

1. Open **Scene Patcher**, click **Refresh** in the command band, and select the marker's row in the outliner. You can also use **Select in world**, as described in [select world targets](select-world-targets.md).
2. On the **Connections** tab, check the stored segment, end, and distance under **Current binding**, below the binding fields. The world marks that track in green while the tab is open. Each **Named location** entry shows a location registered under this marker ID and tells you if it belongs to another object. On the **Object** tab, a missing stored segment appears as a broken binding under **Detected problems** for an enabled marker. For a disabled marker or an empty binding, a **Marker binding note** explains the state.
3. In the **Track location (m)** row, enter the target segment ID in **Segment**, `a` or `b` in **End**, and the distance in metres in **Distance**. The end appears in lower case everywhere, but you can type either case. Segment IDs are case-sensitive, so the game distinguishes `S377` from `s377`. To choose from the world, click **Segment** under **Replacement targets**, click the track, and click **Confirm** in the footer. The fields take the segment, end, and distance at the point you clicked. If you later change the segment field, **Picked** fills it again with the chosen segment.
4. Click **Set** at the bottom of the tab.

Before changing anything, the patcher checks that the segment exists in the loaded track graph and contains the distance. It refuses a missing segment, a distance beyond the segment, or an end other than `a` or `b`. The marker keeps its binding and registration. The patcher never substitutes a nearby segment.

After the check, the patcher removes the marker's named location and writes the new stored location. If the marker is enabled and active, it registers the new named location. It also registers any map label below the marker with the map again, so the label's dimming follows the new location's track group.

The patcher then checks that the stored location matches the mod's location. It verifies that the game has exactly one named location under the marker's ID, that it belongs to this marker at that location, and that every registered map label dims according to the new location's track group. Any difference fails the edit. The patcher restores the earlier stored location and named location.

## Detach the marker

Click **Detach** at the bottom of the tab. This empties the stored location and removes the marker's track location from the game's listing, so looking it up by ID no longer finds it. The object stays where it is and keeps its activity. The scene patch file records an explicit `"detach": true`. Omitting the relationship would leave the binding untouched.

## Undo, clear and remove

Native Undo and Redo reverse and reapply each binding edit together with its registration. To remove only this mod's binding change, click **Clear binding** at the bottom of the **Connections** tab. The marker returns to the binding the loaded map gives it and keeps the mod's placement and activity settings.

To remove the whole modification, click **Remove modification** in the footer. This returns the binding, registration, placement, and activity to the loaded map's values. Other objects' modifications stay applied.

If an edit fails and the patcher can't verify the restoration, it reports incomplete cleanup. Further edits of the marker are refused until **Recheck** in the footer succeeds or the world reloads. Recheck refuses while the named location differs from the stored location.

## Activity and registration

A disabled marker keeps its binding without a registration. When it is enabled, the game registers it at the bound location. An explicit `"active": false` in the same modification removes the registration and keeps the binding.

## Markers with a duplicate marker ID

Scene markers have a duplicate marker ID when more than one holds the same ID. In the base-game map, the active speed sign `Speed - Fontana South Curve (3)` and an inactive yard limit hold `Mg6m`, and six `Whistle Sign W` markers hold `Mkft`. Removing one marker's named location removes every named location under that marker ID on every segment. Disabling or rebinding one holder would therefore remove the other holders' registrations too.

The **Dependencies** section on the **Object** tab of such a marker starts with the duplicate marker ID, the number of scene markers that hold it, and why their edits are refused. For a generic track marker, that line ends with `Give one of them a replacement ID to repair it.`, and the **Object** tab offers **Repair duplicate ID** and **Set ID on the Connections tab**. One sentence per holder follows: first the inspected marker, as **This marker**, then the active holders and the inactive ones last, each with its scene and path, whether it is active, and where it has a registration under the marker ID, or that it has none. A registration under the marker ID that no scene marker holds gets a sentence of its own. A scene marker that is not loaded but still owns a registration under the marker ID also counts as a holder.

While the marker ID is shared, the patcher refuses these edits of every holder, a Generic marker or a crossing pad:

- an activity change, such as **Enable** or **Disable**,
- a binding change: a new location or a detach,
- track following.

A Generic marker can take a replacement ID. To change the activity, binding, or track following of a holder that keeps the shared ID, give every other holder its replacement ID first. Scene patch editing records these prerequisites in `dependsOn`. Once a Generic marker has its own ID, the shared ID no longer blocks these edits of that marker, and the other holders keep their registrations. For the steps and replacement ID rules, see [repair a duplicate marker ID](repairing-duplicate-marker-ids.md). A crossing pad cannot take a replacement ID, so its edits stay refused while another holder keeps the shared ID.

An active generic track marker that can take a replacement ID shows a detected problem on the **Object** tab: `Duplicate marker ID <id> is shared with <N> other scene markers. Give one of them a replacement ID to repair it.` A crossing pad, a milepost, a starter placement marker, or a generic marker that the patcher does not support shows the explanation without a problem. An inactive holder shows the dependency lines only, without a problem.

The footer's message area shows the refusal under its action group, such as **Activity**. It starts with the refused action, such as `Enable refused.`, followed by the duplicate marker ID and every holder's activity. The edited marker comes first and inactive holders come last. The scene, scene patch file, and every holder's registration stay unchanged.

Reloading the map registers the same markers under the same ID again, so it doesn't resolve the problem. Choosing another track doesn't help either, because the edit still removes registrations under that marker ID.

A holder's fixed placement stays editable because it doesn't touch the registration. The exception is a map label below the marker that caches a different track group from the one at its location. Correcting that cache would register the marker again, so the placement edit is refused too. A value equal to the marker's current value, such as `"active": false` on an inactive marker, changes nothing and is accepted.

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

If a marker binding edit is refused, read the feedback beside the binding controls for the operation and reason. Before retrying, refresh the inventory and inspect the marker again, especially after Undo, Redo, or another native edit. Unknown or changed definitions stay protected during scene patch editing. Players aren't asked to edit JSON.

Some markers can emit a game warning before the patcher can apply a mod repair. The patcher attempts an early repair only for a dependency-free relationship-only operation, before the game's original `TrackMarker.OnEnable` check, when the target belongs to a valid scene and the track graph is populated. The game's check still runs. If that route is unavailable, the patcher can remove only the exact current warning row emitted by that check, and only after the applicable operation succeeds and the marker binding and dependent state pass a separate verification. Historical log entries are not changed. Unknown, changed, ambiguous, or unrelated warnings remain. If the patcher cannot establish ownership of a warning window, it leaves the window open; you may close it manually. The patcher does not close player windows.

Recover unknown content or scene patch file format problems in the native map editor. Players should not create or edit JSON to resolve these reports.

Remove a repair with **Remove modification** in the footer. This restores the underlying reference even when its original track no longer exists. For an active marker the game's check reports that broken reference again. Undo restores the repaired binding and registration and reconciles only the unchanged warning emitted for that restoration; Redo restores the broken binding and its warning. Unrelated or subsequently changed warnings remain. If several modifications in the same scene patch file target the object, combine them into one before editing or removing it in the native map editor.

## Limits

The automated tests check set, replacement, detachment, omission, refusal of missing, ambiguous, and out-of-range targets, dependency isolation, failure restoration, guarded retry, Undo, Redo, removal, and reload against a controlled track graph. They also check the dependency lines and refusals for a pair and a group of three holders of a duplicate marker ID, a holder whose activity follows its track group, crossing pads, and a holder that is not loaded, and that no holder loses its registration. They do not load Railroader. One native journey on Railroader with patcher commit `c4b6cb9` inspected the `Mg6m` pair and the six `Mkft` holders, one of them inactive through an inactive parent, and saw Enable and a rebind of the yard limit and a Disable of a whistle sign refused while every holder's registration stayed the same. That journey used the earlier wording of the explanation.

According to the game's source, changing a marker's stored track location doesn't change its named location. The patcher therefore adds or removes that registration as the marker does when enabled or disabled.

One native journey on Railroader 0.6.2 with patcher source `a59b8d9` exercised the yard limit Mkwd in the native map editor of a test mod, with four other inherited signs as unchanged controls. Set, replacement, detachment, refusal of a missing segment and of a distance beyond the segment, a placement change, native Undo and Redo, clearing the mod's binding, physical snap, Save, native map editor reload, and removal behaved as described above. In each bound state, the game registered exactly one named location under the marker's ID, it belonged to the marker, and looking up the marker by ID returned that location. A detached marker had no named location, and the controls kept their bindings, registrations, and placements. That journey did not cover a completed transform-gizmo placement of a bound marker, which it refused as a changed target, a map-label refresh, because no admitted sign has a registered map label, or a second application within one load. It covers that marker, build and fixture only.

The game reads a Generic marker's binding in these places:

- Track scripts look up the named location for a marker ID and fall back to the marker's stored location.
- The map dims a label below a marker by the track group of the marker's location, cached when the label registers. The patcher refreshes that cache.
- A new-game car placement reads its marker's location when the setup runs. Such a marker is a starter placement marker with its own family; see [starter placement markers](inspect-scene-objects.md#starter-placement-markers).
- The flare and planner queries read registrations of their own marker types, not of Generic markers.
- `Track.TrackRelativePosition` moves its object only when its relative placement is configured again. In the game, map objects do that, not the inherited scene signs.

The inherited Generic signs in the research inventory are the whistle signs M5pp near Sylva and Mm8w at `World/Large Scenery/Ela/Crossing Unprotected - Bryson Everett/Whistle Sign E`, the speed sign Mwp1 at `World/Large Scenery/Alarka Signs/Speed Sign`, the yard limit Mkwd at `World/Large Scenery/Alarka Signs/Yard Limit`, and the station sign Mi4f at `World/Large Scenery/Ela/Signs/Station Sign (1)`. In that journey's inventory, M5pp, Mm8w, Mkwd, and Mi4f carried `track-marker-v1`; Mwp1's activity follows its track group, so that patcher version refused it. Patcher 0.5.0 admits it with its group subscription. Only Mkwd was edited; the other admitted signs are unverified after a binding edit, a Save, and a reload. Segment IDs are unique keys of the loaded track graph, so the game itself has no ambiguous segment target. The ambiguity refusal guards the scene patch file contract.

Rebinding a track-relative object's marker doesn't move the object or run its terrain snap. If its activity follows its track group, rebinding moves the subscription and preserves an explicit disable. Multiplayer isn't covered.
