<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move and rotate with a transform gizmo

Use the map editor's **Scene patches** panel to place an existing object supported by the fixed-placement adapters. Independent boards, supported bridges and supported Generic track markers use their own dependency checks. Other inventory entries remain inspection only.

1. Refresh the inventory and inspect the object, or confirm it through world selection. Use **Focus** in the command band to bring it into view.
2. On the **Placement** tab, select **Gizmo**, then **Move** or **Rotate** in the footer. The native gizmo uses world axes. Drag an axis, plane or rotation ring in the world. The object previews the result through its placement adapter.
3. Release the pointer to complete that gesture. The gizmo closes. Start another gizmo gesture for the next movement or rotation. Each completed gesture is one operation in the editor's native Undo/Redo history, however small its rotation. A gesture that ends where it began, within 0.01 m and 0.01 degrees, changes nothing and records no operation.
4. Use native **Undo** to restore the prior effective placement, and **Redo** to reapply the result. Refresh the inventory before another edit after using history.
5. Use the editor's **Save** button. It writes the active edited MapMod's `Data/scene-patches.json`, required patcher dependency metadata and native map data. Check outstanding-write feedback after Save. A partial failure keeps completed writes identified and leaves outstanding writes available for retry.

The document stores `placement.mode` as `fixed`, with a map-coordinate position and quaternion rotation. The display converts that position through the current local origin. An origin shift moves the gizmo's local frame without changing the authored map coordinates. A cumulative drag delta is relative to the gesture's starting map pose; it does not accumulate from earlier preview frames. See the [v1 document reference](scene-patch-document-v1.md).

Physical placement does not create, repair or detach a logical binding. On a track marker, a gesture moves the object and leaves its track location and graph registration unchanged; a binding authored in the same modification stays in it. Change the binding as described in [repair, replace or detach a track-marker binding](repairing-track-bindings.md). Bridge placement includes its collider and culling effects while separate track-owned scenery stays in place. Board previews refresh culling and mask registration; [terrain output](moving-boards.md) can remain outstanding after the gesture.

## Cancel a pending gesture

Use **Cancel** in the footer, which replaces the action buttons while the gizmo is active, or Escape to restore the captured state without adding a patch modification or history operation. Closing the panel, starting world selection, choosing another inventory object, leaving the editor, losing focus or losing the pointer/camera also cancels the pending gesture. Save and native history actions cancel a pending gesture before continuing.

While the gizmo owns input, native map picking and editing shortcuts pause. The camera remains separate from that input route. Complete or cancel the gesture to resume native editing. A native pointer operation or field edit must finish before the patcher can start a gizmo.

Starting a gizmo also discards any prepared [physical snap](snapping-to-track.md). Choosing **Track** or **Node** in **Snap** mode starts world selection and cancels the active gizmo before that selection begins. The snap controls return after the gizmo ends; a completed snap and a completed gesture remain separate operations in the shared native history.

A changed, missing or ambiguous target refuses the edit. If another operation changes the object during a preview, cleanup does not overwrite that independent change. An unverified restoration guards further edits to that object. Refresh and inspect it, then use **Recheck** on the **Changes** tab, or reload the editor if its dependent state cannot be verified.

Save does not reload the world. A later load applies the saved fixed desired state. Reloading the editor ends its native history. Current automated checks cover controlled placement, origin, persistence, history and failure scenarios. They do not establish native gizmo rendering, input coexistence or a Railroader Save/reload journey on a particular game build.
