<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Complete an edit with a guided task

Open **Scene patches** in the native map editor, inspect the object you want to change and select **Guided** beside its name. You can move or rotate the object, snap it to a track or endpoint, enable or disable it, set or detach its track binding, or place a starter on track. Tasks that are unavailable stay visible. Their tooltips explain why. If the **Object** tab reports a track binding problem that the object supports repairing, you can also start with **Repair with guided task**.

1. In **Target**, choose a task. It starts with the inspected object's values and keeps that object as its target until you finish or cancel the task.
2. In **Values**, enter your changes. For a snap, select **Track** or **Node** in the footer, click in the world and select **Confirm**. For a binding or starter placement, enter the segment, end and distance. You can also select **Segment** in the footer, click a track, confirm it and select **Picked** to fill in the values. The click supplies the segment, end and distance for both tasks.
3. Select **Next** to open **Review**. It shows the inspected values beside your proposed changes and lists the settings the task will leave unchanged. If you try to select another object during **Values** or **Review**, a message explains why you cannot. The target and your draft stay in place.
4. Select **Apply** in the footer. Guided tasks use the same operations as **Inspector**, with the same requirements and reasons for refusing an edit. Starter placement checks the whole car cut. Changing a crossing pad's binding regenerates the pad along the track at its marker's new location.
5. When the edit succeeds, select **Back to inspector** to inspect the same object, or **Another guided task** to return to the task list. Use the native **Undo**, **Redo** and **Save** controls as you would after an Inspector edit.

You cannot apply a numeric placement, activity or binding draft that matches the inspected values. The button's tooltip explains why.

## Go back or cancel

From **Review**, select **Back** to return to **Values** and keep your draft. Select **Target** to discard it and release the target. **Back** from **Values** also discards the draft and releases the target.

Selecting **Inspector** cancels an unfinished task without applying the draft and restores the previous inspector tab. Closing the window also discards the task.

During **Values** and **Review**, **Clear** or a click on the target object's row cancels a pending world choice while keeping the target and draft. Without a pending world choice, neither action is allowed, and the draft stays in place.

With the pointer over the window, Escape closes an open choice list first. Further presses cancel a drag or a pending world choice, gizmo or snap, one action per press. Escape never ends the inspection or the guided task.

## Recover from a refused or failed edit

If the target is stale or the edit is refused or fails, the task stays in **Review**. Read the result in the footer.

- For a stale target, select **Target**, then **Refresh**, inspect the object and start again.
- For a refused operation, select **Back** to correct the values or **Target** to restart.
- After incomplete cleanup, return to **Inspector** and use **Recheck** before retrying. **Refresh** applies no changes and does not verify cleanup.

Recovery options for the scene patch file are in Session details.

For operation-specific rules, see [numeric placement and native history](editing-placement-in-the-map-editor.md), [snapping](snapping-to-track.md), [track binding](repairing-track-bindings.md) and [starter placement](placing-starter-markers.md).
