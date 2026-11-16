<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move and rotate with a transform gizmo

Use the map editor's **Scene patches** panel to place an existing object supported by the fixed-placement adapters. Independent boards, supported bridges and supported Generic track markers use their own dependency checks. Other inventory entries have no placement edit.

1. Refresh the inventory and inspect the object, or confirm it through world selection. Use **Focus** in the command band to bring it into view.
2. On the **Placement** tab, select **Gizmo**. The native Move gizmo appears on the object at once. Choose **Rotate** with the switch for the rotation rings, and **Move** to return; the gizmo changes at once. The gizmo stays on the object for as long as the **Gizmo** mode is open. The six read-only fields show the object's position and rotation and follow the gizmo while you drag.
3. Drag an axis, plane or rotation ring in the world. The native gizmo uses world axes, and the object previews the result through its placement adapter. Release the pointer to complete the drag. Each completed drag is one operation in the editor's native Undo/Redo history, however small its rotation, and the gizmo stays for the next drag. A drag that ends where it began, within 0.01 m and 0.01 degrees, changes nothing and records no operation; the message area then says that nothing changed.
4. Use native **Undo** to restore the prior effective placement, and **Redo** to reapply the result. Refresh the inventory before another edit after using history.
5. Use the editor's **Save** button. It writes the active edited MapMod's `Data/scene-patches.json`, required patcher dependency metadata and native map data. Check outstanding-write feedback after Save. A partial failure keeps completed writes identified and leaves outstanding writes available for retry.

The document stores `placement.mode` as `fixed`, with a map-coordinate position and quaternion rotation. The display converts that position through the current local origin. An origin shift moves the gizmo's local frame without changing the authored map coordinates. The gizmo stands at the object's marking, also for an object the game does not move with its origin, such as a starter marker under `Setups`. A cumulative drag delta is relative to the gesture's starting map pose; it does not accumulate from earlier preview frames. See the [v1 document reference](scene-patch-document-v1.md).

Physical placement does not create, repair or detach a logical binding. On a track marker, a gesture moves the object and leaves its track location and graph registration unchanged; a binding authored in the same modification stays in it. Change the binding as described in [repair, replace or detach a track-marker binding](repairing-track-bindings.md). Bridge placement includes its collider and culling effects while separate track-owned scenery stays in place. Board previews refresh culling and mask registration; [terrain output](moving-boards.md) can remain outstanding after the gesture.

## End a drag or the gizmo

Press Escape during a drag to restore the captured state without adding a patch modification or history operation; the gizmo stays. Save and native history actions cancel a drag in progress before continuing, as does losing focus or the pointer or camera.

The gizmo leaves when the **Gizmo** mode ends: another placement mode, another tab, another inventory object, **Clear**, closing the panel or leaving the editor. Starting a world choice, such as **Track** in **Snap** mode, takes it away for the length of the choice. It returns when you come back to the mode.

The gizmo takes input only during a drag. While it merely stands on the object, the native Map Editor works as usual: its selection, its own gizmos, its shortcuts and the camera. A drag cannot start while a native pointer operation or field edit is in progress. World click targets, such as a station's click spot, do not react while the gizmo is shown, so a click on a handle never opens a station window.

After a native Undo or Redo the gizmo stays on the object at its new pose and takes a drag again as soon as the object was read again; the line under the mode switch says so meanwhile. An object that cannot be edited has no gizmo: **Gizmo**, **Snap** and **Follow** are inactive for it, with the object's own reason in their tooltips. While the gizmo stands, the game's world click targets rest, with their hover tooltips: a station door neither opens its window nor offers to. The native editor itself keeps working: a right-click on a track still opens the native track menu. A crossing pad has no gizmo: the pad follows its track, so every placement mode is inactive for it and its tooltip says why. Change the binding as described in [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md) to move the pad.

A changed, missing or ambiguous target refuses the edit. The refusal names what changed, such as the position, the rotation or the first binding line that differs. If another operation changes the object during a preview, cleanup does not overwrite that independent change. An unverified restoration guards further edits to that object. Refresh and inspect it, then use **Recheck** in the footer, or reload the editor if its dependent state cannot be verified.

Save does not reload the world. A later load applies the saved fixed desired state. Reloading the editor ends its native history. Current automated checks cover controlled placement, origin, persistence, history and failure scenarios. They do not establish native gizmo rendering, input coexistence or a Railroader Save/reload journey on a particular game build.
