<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Snap an object's position to a track

Use this route in the map editor for an object with a supported fixed-placement adapter. Open **Scene patches**, refresh the inventory and inspect the object. Check its current physical placement and logical relationships before starting.

1. Choose **Choose snap track** to select a track segment, or **Choose snap endpoint** to select a track node. Click the track ribbon or node marking in the world. Navigate with the camera as needed.
2. Check the candidate's ID. A segment also shows both endpoint IDs; a node shows its map position. If candidates overlap, choose one explicitly by identity. Use **Focus candidate** to inspect it.
3. Choose **Confirm identity**. The authoring panel shows the physical snap target and the resulting map position. Confirmation prepares the operation without moving the object or adding history.
4. Choose **Apply physical snap**. A segment snap uses the closest point on that selected segment's cubic curve to the inspected object's position, including the segment ends. An endpoint snap uses exactly that node's position. Both keep the object's rotation, activity and logical binding. There is no lateral or height offset.
5. Use the native **Undo** and **Redo** controls to reverse or reapply the completed edit. Use the native **Save** button to persist it, then reload the world when you need to verify fresh-world reconstruction. Reload ends the editor's Undo/Redo history.

Use **Cancel selection** before confirmation or **Cancel prepared snap** afterwards to discard the pending operation. Closing the patcher panel also discards it. Refreshing or changing the inspected object clears a prepared snap.

Starting **Move with gizmo** or **Rotate with gizmo** discards a prepared snap. Starting a world selection cancels an active gizmo and restores its captured state first. While the gizmo owns input, complete or cancel it before using the snap or numeric placement controls. Snap and completed gizmo gestures use the same native Undo/Redo and Save routes. See [move and rotate with a transform gizmo](using-transform-gizmos.md).

The operation checks the object, loaded map context, selected graph identity, native handle and copied geometry again before mutation. A missing, ambiguous, replaced or changed target refuses the operation. It never substitutes a nearby track. Inspect and select again after correcting the cause. A failed mutation uses the existing placement adapter's restoration. An unverified restoration blocks another edit until an explicit recheck succeeds or the world reloads.

Save writes the resulting pose as [`placement.mode = "fixed"`](scene-patch-document-v1.md). The saved document does not retain a snap target or resolve that track on reload. Later changes to the track do not move the snapped object. Persistent track-following placement is a separate operation and is not implemented by this route. Changing a physical position does not repair or replace a logical binding. On a track marker, a snap moves the object and leaves its track location and graph registration unchanged; change that binding as described in [repair, replace or detach a track-marker binding](repairing-track-bindings.md).

The controlled tests cover selection refusal, fixed-document persistence, origin conversion, repeat application and restoration. Native geometry, visual presentation, native history and fresh-world reconstruction require a Railroader journey on the tested build and fixture; a successful assembly build alone does not verify them. Board placement can also regenerate terrain output; see [move a board with its terrain effects](moving-boards.md). Bridge placement has its own collision and separate-scenery checks; see [move or disable a bridge with its collision](placing-bridges.md).
