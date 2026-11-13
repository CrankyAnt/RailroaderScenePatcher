<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Follow a track or endpoint

This guide shows how to make a scene object follow an explicitly selected track or endpoint, and how the explicit disable of a marker object works when its activity follows a track group. Use it in the map editor for an object with a supported placement adapter: an independent board, the Ela bridge or a Generic track marker.

Track following stores where the object stands relative to the track. When the track moves, the object moves with it and keeps that relative position and orientation. A [snap](snapping-to-track.md) moves the object once and stores a fixed map position instead.

## Make an object follow a track

1. Open **Scene patches**, select **Refresh inventory and verify stored target identities** and select the object's row, or select it in the world as described in [select world targets](select-world-targets.md).
2. Place the object where it belongs relative to the track first, with numeric placement, a snap or a [transform gizmo](using-transform-gizmos.md).
3. Under **Track following**, choose **Choose track to follow** and click a track segment, or **Choose endpoint to follow** and click a track node. Check the candidate's ID and choose **Confirm identity**.
4. Choose **Follow confirmed** followed by that identity. The object stays where it is. For a segment, the patcher takes the location on that segment nearest to the object, measured from end `a`. For an endpoint, it uses the node.

The panel then shows the followed track and the offset in the track's frame. The edit is one native Undo step. Save writes it to `Data/scene-patches.json` as [`placement.mode = "trackFollowing"`](scene-patch-document-v1.md#placement) and raises the patcher dependency to 0.5.0.

While the object follows the track:

- Moving or turning the followed segment or node in the editor carries the object with it a moment later. This changes neither the document nor the Undo history, and Undo of the track edit carries the object back.
- A placement edit, a snap or a gizmo gesture moves the object and stores the new offset relative to the same track.
- **Stop following (keep current pose)** stores the current pose as fixed placement. The object no longer moves with the track.
- **Remove modification** returns the object to its state without this MapMod's modification.

On every world load the patcher places the object relative to the track's current pose. If the followed segment or node no longer exists, or the distance lies outside the segment, the modification fails, the object stays where the game placed it and the patcher reports the failure. It never substitutes another track.

A failed move restores the object's previous state. When that restoration cannot be verified, the object refuses further edits and stops following until **Recheck after failed edit** succeeds or the world reloads.

Track following changes only placement. It does not change a marker's binding, its graph registration or its activity. Change a binding as described in [repair, replace or detach a track-marker binding](repairing-track-bindings.md).

## Disable an object whose activity follows a track group

Some Generic marker objects, such as the Alarka speed sign, carry a `Track.TrackRelativePosition` that switches the object on and off with the track group of the marker's bound segment. The inventory shows `Track-relative targetActiveFollowsTrackGroup=True` for them. The panel then says that the object's activity follows its track group.

- **Disable object** writes `active: false`. The patcher holds the object inactive. When the game enables that track group later, the object stays inactive; the hold also prevents a brief activation while the patch is applied again.
- Without an authored `active` value, the game keeps switching the object with its track group.
- **Enable object** is refused for such an object. Its activity belongs to its track group; use **Clear authored activity** instead.
- **Clear authored activity** removes `active` from the modification. The object follows its track group again and takes the group's current state: it is active when the group is enabled and inactive when it is not.
- **Remove modification** also releases the hold and returns the binding, the group subscription and group-driven activity of the object without this MapMod's modification.

Rebinding such a marker moves its group subscription with it. The object then follows the new segment's group and no longer reacts to the old one. A detached marker follows no group. An explicit disable stays in place through a rebinding. Undo and Redo restore the binding, the subscription and the hold together.

A document with an `active` value on a track-marker object needs patcher 0.5.0, because an older patcher cannot hold a group follower inactive.

## Limits

In the map editor only the active edited MapMod's document applies, and the editor moves its following objects while their track moves. In ordinary play, every applicable document places its following objects from the track's current pose at world load. The automated tests cover track-following reconciliation, repeated application, refusal, restoration, explicit-disable precedence, native group activity and their Undo, Redo, Save and reload with controlled track and group fixtures. A Railroader journey is required for the native track and group behavior on a given build; the tested builds and fixtures are recorded with that journey. Group followers whose relative position activates another object, and objects whose group activation another component replaced, are refused. Multiplayer is not covered.
