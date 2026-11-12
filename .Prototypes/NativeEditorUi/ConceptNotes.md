<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene patches editor UI concepts

**PROTOTYPE.** Throwaway exploration for specification #38. `index.html` mocks everything in memory. It reads and changes no game, save, file or mod. Nothing here is implemented in the product.

## Selected direction

Concept A is the base. It gains one part of Concept C: the step-by-step job, as an optional Guided mode inside the A window. C's ledger, Document tab and docked column are not part of the direction. B and C remain in `index.html` unchanged for comparison. This is the selected interface direction for delivery: the A inspector and outliner with an optional guided task workflow. Further adjustments may refine the design during implementation. The HTML remains a throwaway mock, not production code or evidence of in-game behavior.

### Guided mode in A

- **The switch.** An Inspector and Guided switch sits beside the object name in the inspector pane. Inspector is the default and shows the tabs as before.
- **Entry from a problem.** The Summary tab of an object with a detected problem has a "Fix step by step" button under the problem text. It opens Guided mode on that problem with the object pinned as the target. Whistle Sign W is the example.
- **Entry without a problem.** Switching to Guided lists the tasks for the selected object: change binding, detach, move or rotate, snap to a track, enable or disable. A task the object cannot take is disabled and gives the reason.
- **Steps.** A task has four steps: Target, Values, Review, Apply. When the selected object can take the task, the task pins it at once and opens at Values, or at Review for a task without values. This holds for the task list and for "Fix step by step", and matches Concept C. Target then shows as done, and Back returns to it to change the target, which unpins the object and discards the entered values. A task opens at Target only when no object is selected or the selected object cannot take it. There, Target shows the candidate and pins it on Next. Values holds only the fields for that task, including a world choice. Review shows the inspected scene beside what Apply will write, and names what stays unchanged. Apply shows the result.
- **What stays.** Guided mode replaces only the tabs, the tab body and the footer of the inspector pane. The window, the band with Refresh and state, the outliner with its search and Problems filter, and the object heading stay in place. Back and Next move only the steps.
- **Returning.** "‹ Inspector" at the top of the guided region and the Inspector half of the switch both return to the tabs. After Apply, "Back to inspector" is the primary button and "Another guided task" returns to the task list.
- **Rules reused from C.** A task pins its target and owns the draft for it. It starts from the inspected scene values. Selecting another object during Values or Review changes nothing and shows a note. Returning to the Inspector, going back to Target or switching concept cancels an unfinished task and discards its values. A refusal or failure stays in Review with its recovery action. Guided tasks call the same mock actions as the tabs, so native Undo, Redo and Save treat them the same way.

Open points for the guided mode:

1. Whether "Fix step by step" should also appear in the outliner's Problems filter, or only in the Summary tab.
2. Whether planned specialized operations such as mileposts and crossings should be guided only, or also have a tab.
3. Whether the Apply result step is needed, or the task should return to the Inspector directly with a message.

## Question

How should the Scene patches editor interface be organised so that a map modder can do routine work without reading source code or long instructions, while the world and the native Map Editor stay usable?

The routine work is: find and inspect an object, change a logical binding without moving the object, change physical placement on its own, see specialized operations, recover from a stale, refused or failed edit, and use native Undo, Redo and Save.

## Audience

The primary user is a map modder who knows Railroader's map editor. Identity, scene path and patch JSON stay available for advanced authoring and recovery. Players have separate needs, runtime warnings and installation, which these concepts leave to the game's native warnings and to player documentation.

## How to open it

Open `index.html` in a browser. Without a parameter it shows the overview. `?variant=A`, `?variant=B` and `?variant=C` open a concept. The white bar at the bottom switches concepts, as do the left and right arrow keys when no input has focus.

The bar also holds the scenario controls. "Make stale" simulates a native edit that invalidates the inspected target. "Fail next" makes the next apply fail and restore. "State" prints the shared state. "Fit" shows the whole 2560 by 1440 screen, and "1:1" shows true size with scrolling.

All three concepts read and write one shared state, so you can start a change in one and continue in another.

## What all three share

- One target. Selecting in the world, in a list or in the native editor selects the same object.
- Placement, binding and track-following are separate. Every placement action states that the binding stays, and every binding action states that the object does not move. A dashed line in the world shows the current binding in teal and the draft binding in amber.
- A world choice is a visible pending state with a named candidate, Confirm and Cancel. Confirming sets a draft only.
- An apply refreshes the observation itself. A stale target refuses the apply, keeps the draft and offers "Refresh and inspect again" next to the message.
- Undo, Redo and Save belong to the native Map Editor. The concepts show unsaved state and may repeat the same action, but they add no second history.
- A disabled control gives its reason in a tooltip. Help text sits in tooltips and info marks, not between controls.
- Every operation carries one of three capability statuses, described below.

## Capability status in the mock

The mock separates three statuses. None of them claims behaviour verified in the game.

- **Unmarked.** Placement, gizmo, snap, activity, board terrain output and bridge limits exist in the source snapshot under closed tickets. This prototype did not verify them.
- **Not yet accepted**, marked ◇ or NOT YET ACCEPTED. Marker binding and detachment have controls in the source snapshot, but #45 is open and the operated journey showed refusals and actions without feedback. The successful binding flow in the mock is invented for layout judgement.
- **Planned**, marked ◦ or CONCEPT. Mileposts, crossings, crossing pads and track-following have no implementation. Their actions are disabled.

## Concept A, outliner and inspector

A groups the work by object. One movable, resizable window follows the native master and detail pattern. The left pane has search, an All, Problems and Patched filter, and objects grouped by family. The right pane inspects the selected object under fixed tabs: Summary, Placement, Binding, one specialized tab named after the family, and Identity. A tab that does not apply stays in place, dimmed, with the reason.

The workflow is select, open a tab, edit, apply. Each tab body scrolls on its own. The footer below it holds the actions, the refusal or failure message and the pending world choice, and it does not scroll.

A differs from the others by keeping the object list on screen at all times and by giving every kind of information about one object a fixed tab.

- Gains: closest to the native editor, good for inspecting many objects in a row, stable control positions.
- Costs: the largest window. The list takes space during a long edit. Changes across objects are visible only through the Patched filter.
- Extending it: a new family adds one specialized tab. Many specialized operations on one family, crossings for example, would crowd that tab and need sub-sections.

## Concept B, world-anchored tools

B groups the work around the object in the world and has no main window. A slim bar at the top holds find, the problems list, Refresh, snapshot state, unsaved state and the data sheet toggle. Selecting an object opens a small card next to it. The card offers only that object's operations, Place, Bind, the specialized operation and Activity, and shows one at a time with its own apply row. The user can drag the card away from the object.

Recorded data lives in a separate, optional data sheet window. During a world choice a prompt strip appears and the candidate's label in the world carries its own Confirm.

B differs by putting controls where the user is looking and by splitting data from controls into separate windows.

- Gains: least screen cover, shortest path for spatial work, operations appear only when relevant.
- Costs: weak for browsing and comparing objects. The card's position changes with the selection. More separate pieces to manage. A card near a screen edge needs placement rules.
- Extending it: a new operation adds one segment on the card. A family with many operations would need a second row or a menu.

## Concept C, change sheet

C groups the work by job and around the patch document. A docked column at the right edge collapses to a rail. Its tabs are Worklist, Changes and Document. The Worklist lists detected problems, items worth checking, outstanding output, and the jobs a user can start. A job runs in a panel fixed above the lists with three steps: target, values, and a before and after review that also names what stays unchanged.

A job pins its target and owns the draft for that target. It starts from the inspected scene values. Selecting another object while the job is past the target step changes nothing and shows a note. Cancelling the job, going back to the target step or leaving Concept C discards the entered values. A native Undo or Redo on the pinned target resets the job to the new scene values. These rules are mock behaviour for judging the workflow. The guided mode in A reuses them.

Changes is a ledger. Every applied change, refusal and failure is a row with its saved, unsaved or undone state. Document shows persistence state, Save, the reload boundary and the patch JSON.

C differs by starting from intent or from a problem instead of from an object, and by keeping a visible record of what happened.

- Gains: strongest for repair, review, recovery and Save. Refusals stay visible after the message is gone. Target lists explain why an object cannot take a job.
- Costs: more steps for a small nudge. Free inspection is secondary. The column covers a fixed strip of the world. The ledger must match native history exactly or it misleads.
- Extending it: a new capability adds a job type. Crossing coverage, detection and equipment fit as separate jobs.

## Help, status and data

| Kind | A | B | C |
| --- | --- | --- | --- |
| Data | Summary and Identity tabs | Data sheet window | Selection summary, Document tab |
| Help | Tooltips, info marks | Tooltips, info marks, card hint line | Tooltips, step text, review step |
| Persistent state | Band at the top of the window | Top bar | Column header, ledger |
| Action feedback | Footer above the actions | Card, above its apply row | Review step, then a ledger row |

## Decisions for review

The direction above answers questions 1 and 7: the object is the entry point, and A combines with C's guided job only. Question 4 is answered in part, because the review step exists in Guided mode and not in the tabs. The other questions stay open. The list is kept as the original comparison record.

1. Which entry point fits routine work best: the object (A), the world (B) or the job (C)?
2. Should recorded data share a window with the controls (A, C) or sit in its own window (B)?
3. Should specialized operations appear with the inspected object (A, B) or as separate jobs (C)?
4. Is a before and after review step worth its extra click for every edit, or only for multi-part operations such as mileposts and crossings?
5. Should a record of refusals and failures persist, as in C's ledger, or is a message next to the action enough?
6. Should Save and Undo appear inside the patcher interface as well, or only as state, with the actions left in the native window?
7. The concepts combine. One example is A's inspector with C's ledger as an extra view and B's Confirm on the world label. Which parts belong together?

## Limits

- The backdrop is a still illustration drawn for this prototype. Camera movement, real picking and a real gizmo are absent. Clicking the gizmo arrows nudges the draft.
- Fonts fall back to locally installed faces. Montserrat and Libre Baskerville render only where the viewer has them installed.
- Window positions and sizes last for the session only.
- The product's reported panel slowness is investigated separately. Its cause is not yet attributed, and any repair depends on that diagnosis. This page measures, diagnoses and fixes nothing about game performance, and no layout here is offered as a remedy.
- In A and B a draft belongs to the object and survives a switch between those two concepts. In C a job owns the draft, as described above.
- Validation so far is listed in `ResearchSummary.md`.
