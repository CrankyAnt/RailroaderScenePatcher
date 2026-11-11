<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Inspect scene objects

Open **Scene patches** in the map editor. The native entry button defaults to the right edge below visible native controls. The panel uses the game's window and controls and opens on the right side, leaving the centre of the view free. Drag its title to move it, and use its close control or the entry button to close it. Closing cancels a pending world choice. Your moved or resized window stays in place while it fits on screen and leaves native controls accessible. A screen or UI-scale change, or a native window that actually obstructs it, can require safe reflow. Hovering the entry does not move it to avoid its own tooltip. The layout uses the actual bounds of pointer-responsive native graphics; non-interactive decoration is not detected. When the right-side slot is obstructed, the panel uses another free area. It closes only when no usable area remains. Select **Refresh inventory and verify stored target identities**. Refresh reads the loaded scene and the active edited MapMod's supplied definitions. It does not apply patches, save files, change object activity, repair bindings or regenerate dependent output. Objects need no patch or warning to appear.

Use [world selection](select-world-targets.md) to find an object by its marking or check a replacement candidate against the track and scene.

## Find and inspect an object

Search by name, full hierarchy path, family, binding identifier, dependency or detected problem. The inventory includes active and inactive loaded objects. Select the inventory row labeled `Inspect <family>: <path>` to see the Unity scene, hierarchy path, intended component, map-coordinate position, quaternion rotation, applicable serialized bindings, dependencies and detected problems. **Copy inspected identity** copies JSON values from that observation. A copied identity does not establish support for editing the object.

An independent object has no invented track binding. A missing or empty segment reference appears as a problem. A reference to an existing segment can still name the wrong track. The patcher cannot decide whether a valid binding or physical placement matches the map author's intention. Compare the displayed identifiers and placement with the world.

Supplied definitions remain visible when their target is missing or ambiguous, their modification dependency is missing, or their document cannot be read. Such a target cannot be selected. The referring definition identifies the problem. Inspection does not rewrite an unreadable or newer-format document.

## Focus and refresh

Select **Focus inspected object** to jump the camera to the observed location. Focus keeps the inspected selection and does not author a scene edit. Native camera jumps may shift the floating origin or load terrain. Map coordinates remain the placement reference.

Mouse actions inside the product button or panel belong to that UI. Scrolling an observation should not also zoom the world, and dragging the scrollbar or title should not select objects behind the panel. A drag that begins inside remains captured until release. Outside the panel, normal world navigation remains available.

The panel labels an unscanned or changed inventory as stale. While the panel is open it checks scene and supplied-document changes every two seconds. These checks do not replace the displayed snapshot. Refresh explicitly replaces the observations and retains the inspected object when its handle and component still exist. Refresh after native edits, Undo/Redo, activity changes or document changes. Supplied document observations update only on explicit refresh.

Focus rejects a changed or replaced observation. Before a complete-document placement, the engine repeats unique resolution and preflight immediately before mutation and checks that the map context has not changed. Editor integrations can additionally pass an inspected-target token to reject changes in observed placement, bindings or dependencies. Refresh cannot substitute another object with the same path for a completed edit.

## Coverage and dependencies

| Family or ownership path | Inventory coverage and editing route |
| --- | --- |
| Track markers, including yard-limit and station signs | Typed markers and serialized track-relative placement. The object's label does not establish its marker type. |
| Mileposts | Marker binding, prefix, origin, mileage and component dependencies. Inspection leaves models and mileage unchanged. |
| Track-group-linked visuals | Serialized `TrackRelativePosition` targets, offsets, terrain snapping and group-following settings. Construction-controlled targets retain their separate segment and target relationships. |
| Crossing pads | Markers with the curve-mesh component, including their generated-geometry dependency. |
| Crossing detection | Loaded spans and controller circuit roles. A missing endpoint segment differs from a valid but unintended track. |
| Existing crossing equipment | Controller-bound lamp-material, gate-animation and bell-player components under loaded crossing controllers. Component presence does not prove functional detection, light output or audible playback. |
| Independent boards | Loaded objects with a renderer culler and a circle map mask and no track marker. Those that match the board pattern accept fixed placement and activity, as described in [move a board with its terrain effects](moving-boards.md); the others show the reason in their problems. |
| Retained bridges and construction scenery | The original inventory's identified Ela bridge culler and separate `Game.Mow.TrackConstructionScenery` controller. The controller exposes its segment IDs, construction conditions and referenced targets, including the bridge's steel and abutments. Moving the controller does not imply moving its targets. |
| Generated track trestles | The two `AutoTrestle` controls identified near Ela belong to generated track-segment hierarchies. They remain native track geometry, separately owned from the retained bridge. |
| Industry/passenger spans and map-object anchors | OpsDTO spans and MapObjectSpawner anchors use native map/operations data. This covers the original P7ec/Pfaj, Wilmot and Sox anchor cases. The current Ela passenger span is native operations data; the historical FUSE warning concerns the previous game line. |
| Player-created signs | Use the native sign editor. Its ownership and persistence do not establish an inherited scene-sign editing route. |
| Missing object configurations or industry definitions | The original Sylva diesel-stand and Bryson textile cases concern native configuration/definition data, with no identified scene-object repair. |
| Signals, CTC and interlocking | Their relationships appeared in the historical inventory but the first-version operation boundary excludes their edits. A span's presence in the inventory does not enable these operations. |

Discovery reads loaded objects, including inactive ones. It does not load otherwise absent scenes. Dependencies include child components, serialized marker/span/controller relationships and direct references from loaded scene component fields and lists. Identifier-based or indirect consumers can remain unknown. An unavailable field or graph reports an inspection limit rather than proving a binding valid. Names and proximity never create a functional relationship.

All newly inventoried patterns are inspection-only unless an operation adapter explicitly supports them. The table accounts for the applicable families and ownership cases in the original inventory. It does not promise discovery of arbitrary custom scene hierarchies, require a separate bridge model whitelist, or establish native rendering or gameplay behavior. An unidentified timber object in an earlier bridge image remains unattributed; the two sampled native trestles do not identify that object.

The [candidate ownership findings](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/20#issuecomment-6717432391) distinguish retained signs from native passenger and anchor data. The [operation investigation](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/18#issuecomment-6725711245) records dependency observations and their limits. These research results do not establish production inspection behavior.

The original [candidate families](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/4c00445b048adccb6920c238116d1f7e1e915db9/Research/Scope/CandidateFamilies.md) and [ownership audit](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/f8b3a72f00dc6556e5d37894605b0312658ad598/Research/NativeCapabilityAudit.md) identify the retained bridge family and its native-data distinctions. The later [bridge trial](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/5e99c72dee551762316c0b8cf5aafec5bf40b748/Research/SceneOperationBatch/Evidence/Boot02/Bridge/Report.md) identifies the construction controller's steel and abutment references. The [trestle observations](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/5e99c72dee551762316c0b8cf5aafec5bf40b748/Research/SceneOperationBatch/Evidence/Boot03/Bridge/Report.md) distinguish the sampled track-owned controls from the retained root and the unattributed timber image.
