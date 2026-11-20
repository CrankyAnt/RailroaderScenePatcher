<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move and rotate with a transform gizmo

Use **Scene patches** in the native map editor to place an existing target supported by the fixed-placement adapters. Independent boards, supported bridges, and supported Generic track markers each have their own dependency checks. Other inventory entries have no placement edit in Scene Patcher.

1. Refresh the inventory and inspect the object, or confirm it through world selection. Use **Focus** in the command band to bring it into view.
2. On the **Placement** tab, click **Gizmo**. The native Move gizmo appears on the object immediately. On the switch, click **Rotate** for rotation rings or **Move** to return to movement. The gizmo changes immediately and stays on the object while **Gizmo** mode is open. The six read-only position and rotation fields update as you drag.
3. Drag an axis, plane, or rotation ring in the world. The native gizmo uses world axes, and the object's placement adapter previews the result. Release the pointer to complete the drag. Each completed drag adds one operation to the native map editor's Undo/Redo history, however small its rotation. The gizmo stays for the next drag. A drag that ends where it began, within 0.01 m and 0.01 degrees, changes nothing and records no operation. The message area reports that nothing changed.
4. Use native **Undo** to restore the prior effective placement, and **Redo** to reapply the result. Refresh the inventory before another edit after using history.
5. Use the native map editor's **Save** button. It writes the edited mod's `Data/scene-patches.json`, required patcher dependency metadata, and native map data. Check outstanding-write feedback after Save. A partial failure keeps completed writes identified and leaves outstanding writes available for retry.

The scene patch file stores `placement.mode` as `fixed`, with a map-coordinate position and quaternion rotation. The display converts that position through the current local origin. An origin shift moves the gizmo's local frame without changing the mod's map coordinates. The gizmo appears at the object's marking even when the game doesn't move the object with its origin, as with a marker under `Setups` that no setup holds. The cumulative drag delta is measured from the gesture's starting physical placement in map coordinates, rather than from earlier preview frames. See the [v1 scene patch file reference](scene-patch-file-v1.md).

Physical placement does not create, repair, or detach a logical binding. On a track marker, a gesture moves the object and leaves its stored track location and the track location the game lists for it unchanged; a binding set in the same modification stays in it. Change the binding as described in [repair, replace, or detach a track-marker binding](repairing-track-bindings.md). Bridge placement includes its collider and culling effects while separate track-owned scenery stays in place. Board previews refresh culling and mask registration; [terrain output](moving-boards.md) can remain outstanding after the gesture.

## End a drag or the gizmo

Press Escape during a drag to restore the captured state. This adds no patch modification or history operation, and the gizmo stays. Save and native history actions cancel an unfinished drag before continuing. Losing focus, the pointer, or the camera also cancels it.

The gizmo disappears when you leave **Gizmo** mode by choosing another placement mode, tab, or inventory object, using **Clear**, closing the panel, or leaving the native map editor. A world choice, such as **Track** in **Snap** mode, hides it for the duration of the choice. It returns when you come back to the mode.

The gizmo takes input only during a drag. Between drags, the native map editor's selection, gizmos, shortcuts, and camera work as usual. You can't start a drag during a native pointer operation or field edit. While the gizmo is visible, world click targets, such as a station's click spot, don't react. Clicking a handle therefore never opens a station window.

After native Undo or Redo, the gizmo stays at the object's new placement. You can drag again once the object has been read again. The line below the mode switch reports this in the meantime. An object that can't be edited has no gizmo. Its **Gizmo**, **Snap**, and **Follow** controls are inactive, with the object's reason in their tooltips.

While the gizmo is visible, the game's world click targets and their hover tooltips are inactive. A station door neither opens its window nor offers to. Native map editor actions still work, so right-clicking a track opens the native track menu. A crossing pad follows its track and has no gizmo. All its placement modes are inactive, and their tooltips explain why. To move a pad, change its binding as described in [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md).

The patcher refuses an edit if the target has changed, is missing, or is ambiguous. The refusal names what changed, such as the position, rotation, or first differing binding line. If another operation changes the object during a preview, cleanup preserves that independent change. Further edits stay guarded if the restoration can't be verified. Refresh and inspect the object, then use **Recheck** in the footer. If its dependent state can't be verified, reload the native map editor.

Save doesn't reload the world. A later load applies the saved fixed desired state. Reloading the native map editor ends its native history. Current automated checks cover controlled placement, origin, persistence, history, and failure scenarios. They don't establish native gizmo rendering, input coexistence, or a Railroader Save/reload journey on a particular game build.
