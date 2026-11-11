<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Present editor feature content

Build feature controls in the `UIPanelBuilder` passed to `PatcherBehaviour.DrawWindow` or its feature methods. `NativeEditorPresentation` owns the entry button, visible window, refresh scheduling and disposal. Use `PatcherBehaviour.Button` for a compact action that requests a content refresh after its callback. Use the native builder's `Disable` for an unavailable button. The inventory uses `AddOutlineView` for native virtualized rows, selection, truncation and scrolling. Keep the feature's operation and error handling in its existing owner.

The presentation uses `WindowManager.CreateWindowWithComponent<ScenePatchWindow>`, the manager's window prefab and builder assets, and `BuilderWindowBase` panel ownership. Fields, headings, scrolling and buttons use native builders. Native Save, Undo and Redo keep their existing owners. Existing prompts use the checked native modal factory in `Hooks`; new prompts must preserve their feature's cancellation, outstanding-work and session guards.

Call `RequestRefresh` when external state changes the content. A refresh waits until native input fields lose focus and a left-button drag ends. The presentation retains the outer scroll position during that refresh. Store field values in feature state as they change, before an action can read them. Use a separate native rebuildable section for live search results so typing does not rebuild the search field.

For persistent feature feedback on the closed entry, call `SetEntryStatus(caption, reason)`. Its native text updater and tooltip read the current values without rebuilding the content. The feature owns deriving and clearing that feedback.

Pass window and entry screen bounds to `InspectionInputBlocker.SetRegions`. Native controls own their raycasts. The blocker retains the separate native wheel-input boundary and the world-pick gate. World-space labels and marks remain in `SpatialSelection`.

Use `NativeEditorPresentation.WorldUiBounds` for world-mark clipping and pick exclusion. It reads current native window and visible graphic bounds, including the title, controls and entry, while excluding the invisible input boundary and full-screen event surfaces. Input-region synchronization runs before early input sampling and after native layout. A pick inside current UI bounds leaves pending offers unchanged.

The native close request clears owned input focus immediately. The feature close callback runs only from `OnShownDidChange(false)` after the native close transition. Do not invoke it again from the close request.

Create the presentation for an attached map editor and dispose it when that editor changes or the world unloads. Disposal clears owned keyboard focus, unsubscribes window callbacks, disposes the entry panel and deactivates the owned roots before destroying them. Do not register persistent custom-window definitions or leave callbacks referencing an old editor.

The native construction is internal game integration, compiled against Railroader build 26335745. Compilation and source inspection do not establish native appearance, input routing or cleanup at runtime. The integration journey must compare corresponding controls and states, measure window and chrome bounds at both required layouts, and replay the feature's behavior. Required unmeasured properties and unverified construction remain open acceptance evidence.

Layout checks preserve an unobstructed window's current position and size across native chrome rebuilds and unrelated native drags. Reflow is reserved for actual UI overlap or off-screen bounds and keeps the current size where the available slot permits. Entry placement also retains a safe current slot. Layout uses each graphic's Unity raycast filters, so non-blocking tooltip backgrounds do not displace the hovered entry. World clipping keeps its existing visible-graphic rules. No controls move during a left-button press or drag.
