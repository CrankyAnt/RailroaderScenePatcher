<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Research summary for the UI concepts

**PROTOTYPE input.** This note summarises the findings behind the three concepts in `index.html`. It publishes no research issue and records no production decision.

After comparison, the direction selected for further exploration is Concept A with an optional Guided mode taken from Concept C's step-by-step job. `ConceptNotes.md` describes it. The prototype stays a throwaway mock, and nothing is implemented in the product.

## Sources

- Specification [#38](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/38) and its child issues #39 to #64, for user stories and task scope.
- The product source at commit `a59b8d96b3936172c89ad1140199ab1f4fafad8a`, for the current editor content.
- Two retained captures of the game at 2560 by 1440, canvas scale 1: one of the native Map Editor with a selected object and populated inspector, and one of the native editor next to the current Scene patches panel with its binding and placement controls.
- A retained capture of native UI element properties, for fonts and colours.
- Decompiled native window code, for patterns without a capture.
- Notes from one operated editor journey on the current panel.

No new game session was run for this prototype.

## Task findings

Observed in the specification and issue list:

- The editor work has a dependency chain: discover, inspect, choose and confirm, edit, verify, then Undo, Redo or Save, with recovery or reload when needed. Numeric edits can start from inspection without a world choice.
- Inventory is broader than warnings. It includes inactive, independent, broken and valid but misplaced objects.
- A pending world choice must not change anything, and a missing or ambiguous target must not resolve by proximity.
- Physical placement, snap, logical binding and track-following are separate. A binding change must not move the object.
- Edits revalidate the inspected target and refuse when it is stale.
- Specialized objects add their own operations: mileposts, crossing pads, boards with terrain output, bridges with collision limits, and crossings with coverage, detection and equipment.
- Player-facing diagnostics and installation guidance are separate from the authoring interface.

Observed in the source snapshot: inventory search and inspection, focus and refresh, world selection, numeric placement and activity, gizmo, track and endpoint snap, marker binding and detachment, and bridge and board output information exist as editor content. Presence in the snapshot and a closed ticket are not proof of accepted behaviour, and this prototype verified none of it.

Issues #45 to #48 and #51 to #64 were open when the inputs were gathered. The prototype therefore marks milepost, crossing, pad and track-following content as concept. Marker binding is a separate case: its controls exist in the snapshot, but #45 is open and the operated journey recorded refusals and actions without feedback, so the prototype labels it as not yet accepted.

Observed in the operated journey: the current panel is one long column in a fixed window. Controls and recorded output share that column. The operator had to scroll between an edited field and Refresh. A refused gizmo edit showed no message in the panel. Scrolling lagged once an authored binding existed. That is one qualitative report without timings. A source trace lists possible code paths, but the cause is not attributed and is investigated separately.

## Native pattern findings

Seen directly in the captures:

- The Map Editor is a floating dark, warm window with a thin frame, a centred title and a close control.
- A compact command band holds Undo and Redo, View and Save, selection actions and a segmented tool strip.
- Search and category segments sit above a hierarchical list. The selected row has a pale fill.
- The inspector beside the list has a serif object heading, a small status mark, small uppercase captions, dark input boxes, grouped X, Y and Z fields, inline actions and its own scrollbar.
- The empty inspector shows a short hint about selection.

From the element capture: Montserrat in Light, Regular, Medium and SemiBold, Libre Baskerville Bold, toolbar text at size 16 in a muted cream, lower alpha for disabled text, and compact controls about 24 units high.

From decompiled source only, without a capture of these windows: native windows can be fixed, resizable or auto-height. The car inspector and station window use conditional tabs. Small task windows such as the turntable pair values with nearby actions. Tooltips explain actions, shortcuts and why a control is unavailable.

## What the prototype infers

These are design inferences, not observed facts:

- That the native master and detail pattern suits patcher objects (concept A).
- That small task windows near the object would work with the map editor's camera and input (concept B).
- That a ledger of changes can stay consistent with native history (concept C).
- That a step-by-step task fits inside the inspector pane of concept A without displacing the outliner, and that pinning the target during a task is acceptable next to native selection. No native window observed in the sources has such a mode.
- The exact sizes, spacing, colours and type scale in the HTML. They approximate the captures and are not measured from the game.
- That automatic refresh after apply is acceptable. It was requested in the journey and is not an explicit requirement in the specification.

## Validation of the prototype

Checked:

- The page script ran in Node against stub DOM objects. All three concepts and the overview rendered for every mock object, tab and tool without `undefined`, `NaN` or object leaks in the output.
- The same run drove the shared actions. Confirming a world choice set a draft only. A binding change left the position unchanged, and a placement change left the binding unchanged. A stale target refused the apply. A simulated failure left the scene unchanged and Retry applied it. Save, Undo and Redo updated the unsaved state. The repair job cleared the detected problem.

- A second run after review covered Concept C's job rules. Selecting another object during the Values step and during the Review step left the target, the entered values and the selection unchanged, and apply changed only the pinned target. Cancelling a job, returning to the target step and switching to another concept each reset the job's draft to the scene values. A new job on an object with an older draft started from the scene values. Marker binding rendered with its not yet accepted label in all three concepts.

A bounded headless Chromium render also opened the overview and all three variants at a 1600 by 1000 viewport. Each concept accepted selecting Whistle Sign E and opening its binding task. These views produced no JavaScript page errors or horizontal page overflow. Private screenshots retain this check. This was a browser render check, not a complete interaction test.

A third Node run against stub DOM objects covered the guided mode added to Concept A after that render check:

- In normal mode, A still rendered its tabs, the Problems filter, the ordinary binding and placement controls and the new mode switch. Ordinary binding, stale refusal and Save behaved as before.
- "Fix step by step" appeared only on an object with a detected problem. For Whistle Sign W it opened the guided task at Values with the object pinned. The outliner, filter and band stayed in the output, and the steps replaced the tabs.
- Selecting another object during Values and during Review changed nothing and showed a note. A world choice ran inside the guided region and set task values only. Back from Review kept the values.
- A stale Apply was refused in Review with the recovery action, and a simulated failure stayed in Review. A successful Apply changed only the pinned target, did not move it, cleared the problem and showed the result step. "Back to inspector" returned to the tabs on the same object, and native Undo reverted the change.
- Switching to Inspector, going back to Target and switching concept each cancelled an unfinished task and reset its values to the scene. A guided task started from scene values and ignored an older inspector draft.
- At Target, an ineligible object gave its reason and Next did nothing.
- Concepts B and C rendered and behaved as before, apart from one correction in C: Next from the target step now pins the target.

Not checked:

- A later headless browser exercise rendered the guided mode, selected Whistle Sign W through Problems, opened Values, edited the segment and reached Review. Its combined Back step and draft assertion failed without isolating the differing field. This partial exercise does not verify the complete guided journey. The browser was closed.
- Complete browser task journeys, tooltip behavior, window drag and resize, keyboard handling, and other viewport sizes remain unchecked. The render check does not establish native font fidelity or in-game usability.
- No check ran in the game.
