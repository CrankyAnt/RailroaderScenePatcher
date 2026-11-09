# Authoring interaction prototype

Issue [#8](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/8) owns the product decision. This prototype asks whether a modder can choose an ambiguous approach track, inspect and navigate while retaining the choice, and complete the coverage change as one undoable edit.

Open `Prototype.html` in a browser. It is a standalone schematic state simulation. It does not represent direct editing of the native world and does not validate the desired native-integrated interaction or game feasibility. It has no persistence outside its in-memory simulation.

Browser checks used headless Chromium and mouse input. Choosing T-102, panning and inspecting N-11 retained the pending choice without a scene edit. Adding coverage created one history entry; Save, Undo and Redo changed the simulated snapshots as expected. A separate move, exit and cancel kept the edit, while confirming exit and reopening restored the saved snapshot. The initial page reported no JavaScript errors. The dark appearance was inspected visually. These checks do not establish usability or native behavior, and the full guided walkthroughs were not exercised.

[Native editor findings](NativeEditorFindings.md) records the inspected native paths and their limits.
