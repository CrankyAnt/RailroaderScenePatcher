<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Repair a duplicate marker ID

Use this guide to give a scene marker a replacement ID when it shares its marker ID with another scene marker. The game registers a track marker's location under its marker ID. If two or more scene markers share an ID, changing one removes all their registrations. A replacement ID gives one holder its own ID, so you can change its activity, track binding, and track following without changing the other holders. Replacement IDs require patcher 0.5.0 or later.

The selected marker receives the replacement ID without moving. The other holders keep their marker ID and registrations.

This description of the patcher's behavior comes from its code and automated tests. A run on the base map in Railroader 0.6.3 build 26740326, with the patcher at commit `325ad5f`, observed repairs of `Mg6m` and `Mkft`. See [limits](#limits).

## When to use it

Two groups of scene markers in the base-game map share a marker ID:

- The active speed sign `Speed - Fontana South Curve (3)` and an inactive yard limit hold `Mg6m`.
- The `Whistle Sign W` markers that hold `Mkft`.

Use this guide when either of these is true:

- An active generic track marker shows the detected problem `Duplicate marker ID <id> is shared with <N> other scene markers. Give one of them a replacement ID to repair it.`
- An edit of a holder is refused, such as **Enable**, a new track location, or **Follow**. The refusal starts with `Duplicate marker ID <id> is held by <N> scene markers`, and it names each holder with its activity. For a generic track marker it ends with `Give one of them a replacement ID to repair it.`

The explanation under **Dependencies** on the **Object** tab lists every holder of the duplicate marker ID. Only an active generic track marker that can receive a replacement ID has the detected problem. An inactive holder has no detected problem, but its explanation still ends with `Give one of them a replacement ID to repair it.`, and the **Object** tab offers **Repair duplicate ID** for it. Its **Detected problems** section then reads `None while this marker is inactive. The patcher refuses changes to its activity, binding, and track-following placement until its duplicate marker ID is repaired.` A crossing pad, a milepost, a starter placement marker, or a generic marker that the patcher does not support shows the explanation without the problem and without the repair.

Each repair changes one holder, and each replacement ID belongs to one marker. A group therefore needs a separate replacement ID for each holder being repaired. A holder that keeps the shared ID still has its edits refused while another holder keeps that ID too. See [order a holder that keeps the shared ID](#order-a-holder-that-keeps-the-shared-id) for how to write the file for this case.

## Give a replacement ID in the inspector

1. Open **Scene patches**, click **Refresh** in the command band, and select the row of the marker you want to change in the outliner.
1. On the **Connections** tab, find the **Marker ID** section. Under the caption **Also holding**, the section lists the other scene markers that hold the underlying ID. To give the replacement ID to one of them instead, click **Select** beside its name. The patcher inspects that marker and opens its **Connections** tab. **Select** also closes an open coverage or equipment form.
1. Enter the new ID in the **Replacement ID** field.
1. Click **Set ID**. The result appears in the footer's message area under **Marker ID**.
1. Set the activity or the track location of the marker. Click **Enable** or **Disable** on the **Object** tab, or enter the location and click **Set** at the bottom of the **Connections** tab. The repaired marker accepts these edits. The other holders do not change.

**Set ID** is unavailable if the field is empty, contains only spaces, or holds the marker's current ID. It's also unavailable if the ID begins with a reserved prefix or begins or ends with a space. Its tooltip explains why. The **Marker ID** section is disabled for objects outside the **Track marker** family. Its reason names the object's family, such as **Milepost**, because a milepost or a starter placement marker also shows the marker type **Generic**. It's also disabled for a generic track marker whose underlying ID no other scene marker shares, unless this mod already sets a replacement ID.

After **Set ID**, the caption above the other holders changes from **Also holding** to **Still holding** and includes the underlying ID, for example `Still holding Mg6m, the underlying ID`. The list names the holders that keep that ID. The selected marker now has a unique replacement ID, so its detected problem disappears. The other holders keep sharing the underlying ID until each has its own.

## Give a replacement ID with a guided task

Use this route to have every step of the change checked in a single guided task.

1. Click **Guided** beside the object's name. Or, on the **Object** tab, click **Repair duplicate ID**. The button appears below the **Detected problems** section for a generic track marker whose explanation names a duplicate marker ID, active or inactive.
1. In **Target**, select **Set replacement ID** in the task list.
1. In **Values**, enter the ID in the **Replacement ID** field. The same checks as **Set ID** apply.
1. Click **Next**. **Review** checks the ID against the scene markers and map objects, as **Apply** does. An ID that's already in use is refused in **Review**, with the marker or map object that uses it. **Review** shows the proposed **Marker ID**. It lists the settings that stay unchanged: placement, activity, and track binding. Every other holder keeps its registration.
1. Click **Apply** in the footer. Then click **Back to inspector**, or **Another guided task** to repair the next holder.

The task changes only the ID. To enable or rebind the marker, use a separate task or the inspector.

## Order a holder that keeps the shared ID

Before changing the activity, track binding, or track following of a holder that keeps the shared ID, give every other holder its own replacement ID. Such edits would remove every holder's registration while the ID is shared, so they stay refused. Repair the other holders first, in the inspector or in the file.

The native map editor adds these dependencies for you. When you change a holder's activity, track binding, or track following, it checks for modifications in the same scene patch file that give the other holders replacement IDs and free the underlying ID. It adds those modification keys to `dependsOn`, so a load applies the replacement IDs first regardless of key order. Fixed placement also needs these dependencies if a map label below the holder caches a different track group from the one at its location. Applying that placement rewrites the holder's registration.

The editor removes those keys from `dependsOn` when the holder's modification no longer sets any of these values, or when it gives the holder its own replacement ID. Other `dependsOn` entries stay. Scene patch editing checks only the modifications in the file being edited.

In a hand-written file, you must name those prerequisites yourself. In the modification that changes the holder, list the key of the modification that gives the other holder its replacement ID:

```json
"repair-yard-limit": {
  "target": {
    "scene": "<scene from the inspected identity of the yard limit>",
    "path": "<path from the inspected identity of the yard limit>",
    "component": "Track.TrackMarker",
    "structure": "track-marker-v1"
  },
  "markerId": "Mg6m-yard",
  "active": true,
  "dependsOn": []
},
"keep-speed-sign-on-track": {
  "target": {
    "scene": "<scene from the inspected identity of the speed sign>",
    "path": "<path from the inspected identity of the speed sign>",
    "component": "Track.TrackMarker",
    "structure": "track-marker-v1"
  },
  "relationships": {
    "trackMarker": { "segment": "pi3pzp", "end": "a", "distance": 3 }
  },
  "active": true,
  "dependsOn": ["repair-yard-limit"]
}
```

The segment, end, and distance are examples. Use a track location on your map. A modification that sets a replacement ID needs no `dependsOn` for its own holder. For three holders, the same rule applies. Each holder that changes names the keys of the modifications that give the other holders their replacement IDs.

## Clear the ID, remove the modification and undo

- **Clear ID** in the **Marker ID** section removes this mod's replacement ID. The marker returns to its underlying ID. **Clear ID** is disabled with the general edit reason first. Then it is disabled with `This mod sets no replacement ID for this marker, so there is nothing to clear.` when the mod sets none.
- **Clear ID** is refused while the modification still sets **Enable** or **Disable**, a track binding or track following, and another scene marker holds the underlying ID. The message reads `Marker ID <id> is also held by <scene/path>, so this marker's activity, binding, and track-following settings need its replacement ID. Clear those settings first, or click Remove modification.`
- **Clear ID** is also refused while another modification names this one in `dependsOn`. The message reads `This marker's replacement ID cannot be cleared because <key> names its modification in dependsOn: another holder's activity, binding, or track-following settings need this ID. Change or remove that modification first.`
- **Set ID** with the marker's underlying ID returns the marker to that ID, as **Clear ID** does, and is refused for the same two reasons.
- **Remove modification** in the footer removes the whole modification from this mod. The marker returns to the underlying ID, track location, and activity of the loaded map, with its registrations. It is refused while another modification names this one in `dependsOn`, with the message `Modification <key> cannot leave the scene patch file because <key> names it in dependsOn; change or remove that modification first. Nothing was changed`.
- Native **Undo** returns the effective underlying ID and its registrations. Native **Redo** applies the replacement ID again.
- If a change fails after the patcher has changed the ID, the patcher restores the earlier ID and its registration. If it cannot confirm the restoration, the footer reports `failed; cleanup is incomplete` with the line `Marker ID: now <id>, before <id>`. Further edits of that object are blocked until **Recheck** verifies it, or until the world reloads. The **Guide** button in the footer opens this guide for a failed marker ID edit.

## Write the modification by hand

Copy the identity with **Copy ID** in the command band. Add a modification to the `modifications` object of `Data/scene-patches.json`, under a key of your choice, as described in [scene patch file v1](scene-patch-file-v1.md#replacement-id):

```json
"repair-yard-limit": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "<path from the inspected identity>",
    "component": "Track.TrackMarker",
    "structure": "track-marker-v1"
  },
  "markerId": "Mg6m-yard",
  "relationships": {
    "trackMarker": { "segment": "pi3pzp", "end": "a", "distance": 3 }
  },
  "active": true,
  "dependsOn": []
}
```

The segment, end, and distance are examples. Use a location on the track of your map. The ID must not be used by any other scene marker, by a graph registration of an object that is not a scene marker, or by a map object of the loaded map context.

Without `markerId`, the marker keeps its ID. A modification with `markerId` requires patcher 0.5.0 or later. The native map editor's **Save** writes the members in this order: `target`, `markerId`, `relationships`, `starterPlacement`, `placement`, `dependsOn`, `active`. It also sets the patcher dependency to 0.5.0 or later for such a file. For the dependent modifications, see [order a holder that keeps the shared ID](#order-a-holder-that-keeps-the-shared-id).

## Rules and refusals

The patcher checks these rules before changing anything. If it refuses the edit, the marker and its registrations stay as they were.

- **Target.** Only a generic track marker takes a replacement ID: a target with the structure `track-marker-v1`. A modification with `markerId` on any other target fails with `Only objects in the Track marker family can have a replacement ID.` Such targets include crossing pads, mileposts, starter placement markers, and objects that are not markers. The other modifications of the file still apply.
- **Empty and invalid values.** The patcher refuses an empty or whitespace-only ID and any value that isn't a string, including `null`. It also refuses an ID that begins or ends with a space because it would look like another ID.
- **Reserved prefixes.** An ID that begins with `passengerStop:` or `flare:` is refused. The game uses these prefixes for its own track registrations.
- **Uniqueness.** The ID must not be used by another scene marker, by a graph registration of an object that is not a scene marker, or by a map object of the loaded map context. The refusal names the user, for example `Replacement ID <id> is already used by the scene marker <scene/path>. Choose an ID that no other scene marker uses.`
- **Map objects not loaded.** When the game has not loaded the map object manager, every replacement ID is refused with `The game hasn't loaded the map objects in the map context, so replacement ID <id> couldn't be checked against them. Try again after the map has loaded.` The patcher does not treat an unchecked ID as free.
- **Load order.** The check uses the world's state when the modification applies. If an earlier scene patch file in load order has already given the ID to a marker, the patcher refuses the later modification. The earlier modification stays applied.
- **One scene patch file.** If two modifications of one scene patch file give the same ID, both fail with `Replacement ID <id> is used by more than one modification; a replacement ID names one marker in its scene patch file`.
- **Order within a modification.** The replacement ID applies before the marker's track binding, its track following, and its activity. One modification can therefore give the ID and then enable, rebind, or make the marker follow a track. Without the ID, the game-registration rule refuses that edit for a holder of a duplicate marker ID.
- **Other holders.** If the marker is active and its binding resolves, its registration moves to the new ID. Every other holder of the old ID keeps its registration.
- **Editing.** Scene patch editing offers the **Marker ID** section only for a generic track marker whose underlying ID another scene marker shares, or whose modification already sets a replacement ID.

## Limits

- A run on the base map in Railroader 0.6.3 build 26740326, with the patcher at commit `325ad5f`, observed the game's registrations. The Yard Limit took `Mg6m-yard`, **Enable**, and a binding at `pi3pzp` in one modification: it registered under `Mg6m-yard` there, and the speed sign stayed registered under `Mg6m`. Undo and Redo, a dependent rebind of the speed sign, Save, and a world reload reproduced both registrations, and removing the modifications returned `Mg6m` to its registration before the repair. A `Whistle Sign W` at Bryson Everett that took a replacement ID and **Disable** left the other four holders of `Mkft` registered. The run did not cover track following, Lua scripts, or multiplayer. The automated tests check these rules against a controlled marker world with the `Mg6m` pair and a group of three holders of `Mkft`, including a replacement ID that applies before track following and the save and reload of dependent binding and track-following edits.
- The patcher doesn't check Lua scripts that look up a marker by ID, such as scripts that call `NativeTrack.GetMarkerLocation`. It doesn't check third-party mods that look up a marker by ID either.
- Map object IDs come from the game's map object manager for the loaded map context. While the manager is not loaded, the patcher refuses every replacement ID.
- A scene marker that is not loaded but still registers under the shared ID counts as a holder. A repair of another holder does not move its registration.
- Two holders of the same ID on the same segment share one graph key. The holder that does not own that key fails the registration check before its replacement ID can apply. This does not happen for the `Mg6m` or `Mkft` groups in the base-game map.
