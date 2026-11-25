<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Inspect scene objects

To open the **Scene Patcher** window, click the **Scene Patcher** icon in the top-right icon strip of the native map editor. The icon appears only in the native map editor. If the strip has no room for it, a floating **Scene Patcher** button replaces it. **Diagnostics** in the session details explains why.

The window opens flush with the left screen edge, directly below the native Map Editor window, and extends to the bottom of the screen. Its default width is 734 units, or the Map Editor window's width if that's greater. On shorter displays, such as a 1080-high screen, it opens at the top right below the button strip and status readout. The default side stays the same across UI scales as long as the minimum window height fits there.

Drag the title to move the window. Use its close control or click its entry again to close it and cancel any pending world choice. The window keeps the position and size you choose, even when a native window or menu opens over it. It moves automatically only if a screen or UI-scale change leaves it partly off screen.

The minimum size is 704 by 470 units. This leaves the outliner, command band, and a tab body in view, with independent scrolling for the tab body and message area. If the panel was open when you left the native map editor, it opens again when you return, once the world has finished loading.

The title bar has four glyphs to the left of the close control. Each has a tooltip with its name:

- **?** opens help for the whole window.
- **Reset layout**, a round arrow, restores the window's default position and the outliner width. It also puts the help and the session details back beside the window. In the compact window, it returns to the full window, and the compact window opens at its default place and height next time.
- **Session details**, a page, opens or closes a separate window with the edited mod's scene patch file, supplied definitions, diagnostics and, during a world choice, its candidates. You can move and resize this window. It keeps its position and closes with the panel.
- **Compact window**, four corners pointing inwards, switches to the compact window. In the compact window, the glyph shows four corners pointing outwards and is named **Full window**; click it to show the full window again.

### Use the compact window

The compact window is a narrow side rail at the right edge of the screen, bottom-aligned, that leaves more of the world in view. It follows the tab or guided task that was open in the full window and keeps the inspected object, a pending world choice, and a placement in progress.

From top to bottom, the rail shows:

- **Refresh** with the two status lamps, and **Select in world**, **Focus**, **Clear**, and **Copy ID**.
- The object's name with the open tab, or **Guided**, at its right, and the object's state line.
- A few lines about the object for the open tab, such as its position, rotation, and **Enabled**, or its track binding. On the **Placement** tab, the rail shows the tab's own controls.
- The message area, six lines high, and one row of actions. During a world choice, the row holds **Confirm**, **Cancel**, **Focus**, and **Details**.
- The outliner's object count with **Hide outliner** at its right, and the outliner rows. By default, the outliner shows six rows.

The compact window has no tabs, search field, outliner filters, **By category** and **By place** buttons, or **Reset filter**. The outliner shows the same tree as the full window, in the view chosen there. A filter or search set in the full window stays active, and the object count shows it in amber. To change tabs, filters, views, or modes, or to finish a guided task, switch to the full window.

The outliner shows only whole rows, and scrolling stops on a row. When you make the compact window higher, the outliner shows one more row for each row's height you add, and the message area takes the rest. Click **Hide outliner** to make the rail lower; click **Show outliner** to show the rows again.

You can drag and resize the compact window. It keeps that place and size until you leave the Map Editor or click **Reset layout**, also when you change the UI scale, and the full window keeps its own place and size. The patcher always starts in the full window, and the full window's place is the one saved for the next session. Each time you switch back to the full window, the help and the session details move beside it, wherever you placed them before. When a scene patch file needs a decision in the **Version choice** or **Repair** panel, the window switches to the full window.

Click **Refresh** in the command band to read the loaded scene and the edited mod's supplied definitions and verify stored target identities. Refresh doesn't apply scene patches, save files, change object activity, repair bindings, or regenerate dependent output. An object can appear without a scene patch or warning.

The command band across the top is split by the divider between the two columns. Above the outliner are **Refresh** and two status lamps: a dot for the object list and a dot for saving. **Current** and **Saved** are green. **Stale**, **Paused**, **Unsaved**, and **Blocked** are amber. Resolve the file issue when inspection is paused. Hover over **Blocked** for the reason you cannot save. The lamps are informational, not buttons, and their tooltips give the reason.

Above the inspector are **Select in world**, **Focus**, **Clear**, and **Copy ID**. **Clear** ends the inspection and cancels a pending world choice, gizmo, or snap. Applied changes stay. During guided **Values** or **Review**, click **Clear** or select the pinned object's row to cancel a pending world choice and keep the target and draft. With no pending world choice, **Clear** is unavailable and its tooltip reads **Unavailable: No world choice is pending.** Selecting the pinned object's row gives feedback and keeps the draft. Return to **Inspector** to cancel the task. Outside guided **Values** or **Review**, selecting the inspected row does the same as **Clear**.

With the pointer over the window, Escape first closes an open choice list, then cancels a drag or a pending world choice, gizmo, or snap, one action per press. It keeps the inspection. With the pointer over the world, Escape cancels a pending world choice or an active gizmo.

Commands that cannot run remain in place, unavailable. **Select in world** is unavailable during a pending world choice; **Focus** and **Copy ID** are unavailable when nothing is inspected. **Copy ID** is also unavailable when the edited mod's scene patch file cannot be read, and **Clear** when nothing is inspected and no world choice is pending. Its tooltip then gives only the reason, after **Unavailable:**, as one plain sentence without a label of its own.

Each **?** opens a separate help window beside the panel, at the height of that **?**, with an introduction, short explanations, and links to the guides. A long help, such as the outliner's, opens in a wider window so that it fits the screen. If it still doesn't fit, scroll the help window with the mouse wheel or its scroll bar to read the rest. Only one help window can be open at a time. It never covers the panel's content or the title bar of the Map Editor window, so the Map Editor's close button stays in reach. When you change the UI scale, an open help window takes the size and place that fit the new scale. A tab's help closes when you open another tab. Click the same **?** again or use the help window's close button to close it.

Below the command band, the outliner on the left has a search field, a **?** for outliner help, the object tree, and a count in the form `N of M objects`. The **By category** and **By place** buttons choose how the tree is arranged. Use its filters to choose what appears:

- **All** shows all objects.
- **Changed** shows objects the edited mod changes.
- **Disabled** shows objects that are switched off, regardless of their parents' state.
- **Problems** shows objects with a detected problem.

Click **Fit** at the right end of the outliner's top row, beside the divider, to fit most names. You can also drag the divider between the outliner and inspector. **Fit** limits the column to 40 percent of the window. If the names need more room, a message with the source **Fit** reports their width. Three notes at the bottom explain that Undo, Redo, and Save are the Map Editor's own, when to refresh after native edits, and that reloading clears the Undo history.

An object opens in **Follow** placement mode if it follows a track, or **Numeric** otherwise. The **Inspector** and **Guided** segments at the right end of the state line switch between inspection and guided tasks. See [complete a guided task](completing-guided-tasks.md).

The inspector on the right shows the object's own name. Its tooltip gives the full path, or the full display name for added equipment. The state line shows `* CHANGED`, or `× REFUSED` or `× NOT APPLIED` for a modification that doesn't apply. It lists the problem count first, followed by the family and **Inactive**. For a part of a baked scenery group, the family reads **Part**, as it does in a world choice. Below it are four tabs, **Object**, **Placement**, **Connections**, and **Changes**. Each starts with a one-line description and a **?** at the right end of the row for the tab's help. Actions sit in one row at the bottom, below the scrolling content. Each tab body scrolls independently. The window itself doesn't scroll.

Every tab has the same footer. Its message area sits above **Remove modification** and **Recheck**, which act on the inspected object from any tab. The message area keeps its height as messages appear and disappear. Scroll to read messages below its bottom edge.

The first message explains what the window is waiting for, such as a world choice, gizmo, or snap, or why the current tab can't edit the object. Below it is the last action's result or refusal, with the action group in bold as its source. If that action later produces a result such as terrain output without another command, the new message appears above the action result.

A result disappears when you start another action, use native Undo or Redo, or inspect another object. A Gizmo result keeps the earlier Copy ID result below it. Changing the tab or placement mode also clears that tab action's result, but keeps results from **Remove modification** and **Recheck**. During a pending world choice, the footer row holds **Confirm**, **Cancel**, **Focus**, and **Details** instead, and the tab controls stay unavailable until the choice ends. An active gizmo puts its **Cancel** button in that row.

Detected problems depend on how the game uses an object. Except for starter placement markers, an object that is switched off itself has no problem of any kind. The game ignores it until someone enables it. Find these objects under **Disabled**. An object hidden by a culler while the camera is far away isn't switched off. The rules differ by kind:

- A crossing pad needs its binding: an empty one, or one that names a segment that no longer exists, is a problem.
- A starter placement marker needs its binding whether it is enabled or not, because a new-game setup reads it either way: an empty binding, or one that names a segment that no longer exists, is a problem. This is the one exception to the rule above; see [starter placement markers](#starter-placement-markers).
- A yard limit, speed, station, or whistle sign, a milepost, a marker under `Setups` that no setup holds, and any other Generic marker need no binding. An empty binding is shown as a **Marker binding note** on the **Connections** tab, not as a problem. A stored segment that no longer exists is a problem while the object is enabled, and [repairing the binding](repairing-track-bindings.md) corrects it.
- An active generic track marker that shares its marker ID with another scene marker, and can take a replacement ID, is a problem. [Repairing a duplicate marker ID](repairing-duplicate-marker-ids.md) gives one holder a replacement ID. An inactive holder of a shared ID is not a problem; its dependencies still name the shared ID, and a generic track marker's dependencies offer the repair. Its **Detected problems** section says that it has no problem while it's inactive and that its edits stay refused until the duplicate marker ID is repaired.
- An independent board has no track binding and no binding problem.

A problem is a defect you can repair with the patcher, such as a broken binding. Four other things are shown and are not problems:

- Why an object of a supported family cannot be edited: **Supported edits** on the **Object** tab reads **Not editable:** with the reason. The object stays in the outliner under its category and place. One such reason is a hierarchy path that two objects share: a patch names its target by scene, path, and component, so it cannot name one of them. Each of the two rows can still be inspected.
- An object of a kind that the patcher cannot yet edit and the Map Editor does not edit. The outliner lists these objects under their category, marked `inspection only`. Their rows have no problem marker or edit. Search and the **All** and **Disabled** filters include them, and the **Object** tab starts with the reason the kind is not yet supported. In the current version, these objects include track spans that do not serve as a crossing controller's detection spans, markers of a type other than Generic, and baked scenery groups that no category names. A baked scenery group that a category names stays in the **Baked scenery group** family under its category, marked `inspection only`, until an edit supports it.
- What the inspection could not read or check, such as an incomplete reference scan or a segment that could not be checked: **Inspection notes** on the **Object** tab, without a problem marker, because nothing there can be repaired with the patcher.
- What the inspection found that isn't a problem on this object: **Information** on the **Object** tab, without a problem marker. An example is the detection findings of a crossing controller that can't be edited, which name the reason. See [detection that no longer fits its track](correcting-crossing-coverage.md#detection-that-no-longer-fits-its-track).

Some scene objects are not listed at all: everything below the `CTC`, `CTC Auxiliary`, `Setups`, `Ops`, `MapFeatures`, `Progressions`, `MapManager`, and `Editor Directional Light` scene roots apart from starter placement markers, signal markers anywhere, starter placement markers that no active setup uses, the map objects whose track anchor the native map editor edits (water, coal, and diesel facilities) and other objects of the map's `Map.json`, track spans the game builds from the map's operations data, map labels, objects the game creates while it runs, the builder photo rig, and objects without a renderer, collider, or light that no family covers.

Use [world selection](select-world-targets.md) to find an object by its marking or check a replacement candidate against the track and scene.

## Find and inspect an object

Search by name, full hierarchy path, family, binding identifier, dependency, or detected problem. The inventory includes active and inactive loaded objects. The tree lists each object under its category and kind, or under its place in the **By place** view, in alphabetical order. For example, the Ela bridge and its construction controller stand under **Bridge** > **Rail bridges**. A newly inspected object's folders open once, so its row shows. Select the object's row in the outliner to inspect it.

Markers before the name show the object's state:

- A red `!` marks a detected problem.
- An amber `*` marks an object this mod changes.
- A red `×` marks an object whose modification in this mod is refused or otherwise not applied.
- A green `+` marks an object this mod adds.

The native map editor puts `*` and `+` after a name. Here they come first so they're still visible when a long name is cut off. The state line under the object's name repeats the state with the badges `* CHANGED`, `× REFUSED` or `× NOT APPLIED`, and `+ ADDED`. For a modification marked `×`, the **Changes** tab gives the reason instead of listing changes, such as `Refused.` or `Failed and restored.` A modification whose cleanup is incomplete keeps its own message.

These markers don't mean work is unsaved. Only the **Unsaved** lamp shows that. `* CHANGED` means this mod's scene patch file changes the object. Setting a value to the object's value without this mod removes that setting from the modification. For example, enabling an object you disabled leaves it unchanged again. An edit control is inactive while its value matches the object's current value.

A category shows kinds only where at least half of its objects share a kind with another object, such as **Rail bridges** and **Road bridges** under **Bridge**. Baked scenery groups stand under their categories, with each group's parts beneath them, as [selecting in the world](select-world-targets.md) describes.

An inactive object's name is italic and followed by `inactive`. A row's tooltip gives its name and path, its category, kind and place where it has them, and when they apply, its inactive state, its inspection-only reason, why this mod changes it, and its problems. Use the inspector tabs to read the object's values:

- **Object** shows its scene, component, structure, and path, whether it is **Enabled**, its supported edits, detected problems, and dependencies.
- **Placement**, in **Numeric** mode, shows its position in map coordinates and rotation in degrees. Editable objects have six fields. Other objects have two lines of text.
- **Connections** shows its track binding and observed bindings.

**Copy ID** in the command band copies JSON values from the observation. The footer's message slots then name the object whose identity was copied, and with **Debug logging** on, so does the Player log. Copying an identity doesn't establish that the object can be edited.

An independent object has no invented track binding. A missing or empty segment reference appears as a problem. The construction segments of a rail bridge are listed on the **Connections** tab, and one that no longer exists keeps its ID with the remark `no longer exists`. It is a problem of the construction, which you repair on its **Connections** tab, and the parts of the construction show the remark without a problem; see [repair the construction segments of a rail bridge](repairing-construction-segments.md). A reference to an existing segment can still name the wrong track. The patcher cannot decide whether a valid binding or physical placement matches the map author's intention. Compare the displayed identifiers and placement with the world.

Supplied definitions remain visible under **Supplied definitions** in the session details when their target is missing or ambiguous, their modification dependency is missing, or their scene patch file cannot be read. Such a target cannot be selected. The referring definition identifies the problem. Inspection does not rewrite an unreadable or newer-format scene patch file.

## Starter placement markers

A setup places the starting cars on the track in a new company game. Each car placement reads its starter placement marker's track location. The front of the cut stands at that point, and the rest extends toward the named end of the segment. The marker's object position doesn't affect this placement and can be hundreds of meters from where the cars appear.

The outliner lists these markers under the **Starter placement** category, once each:

- A marker that a car placement of an active setup holds is listed, enabled or not, because the game reads it at a new game.
- A marker that only setups that are switched off hold is not listed: the game never reads it.
- A marker that no setup holds is no starter placement marker. It stays an ordinary marker in the **Track marker** family, whatever its name or place under `Setups`.

The **Object** tab lists under **Used by** every setup and car placement that reads the marker, with the cars of each cut and whether the placement is a wreck. A listed marker needs a usable binding whether it is enabled or not: an empty binding, or a stored track location whose segment no longer exists, is a problem. A binding on an existing segment shows no problem.

Inspecting a starter placement marker marks its track point in gold with the label **Front of the car cut**. A gold ribbon extends back along the track to show the cut's direction. If the cut's length can't be read, only the point appears, labeled **Track point**. The **Placement** tab shows its map coordinates under **Track point**.

Click **Focus** to move the camera to the track point. If the marker's track location doesn't resolve on the loaded track, **Focus** is unavailable and its tooltip explains why. A stored distance beyond the segment's end also has no track point, so **Focus** is unavailable. In that case, the marker shows no problem because the binding check only asks whether the segment exists.

If the new-game setups can't be read, no markers appear as starter placement markers. Every marker then has an inspection note explaining this limit, and **Diagnostics** in the session details reports it after **Refresh**.

Use the **Connections** tab to choose where the starting cars appear and whether they start as a wreck, as described in [place a starter placement marker on the track](placing-starter-markers.md). The marker object's placement, gizmo, and activity controls stay unavailable, because the object's placement and activity do not change where the cars start; their tooltips say so.

## Focus and refresh

Click **Focus** in the command band to jump the camera to the inspected object's current position in map coordinates. For an object the game does not move with its floating origin, such as a marker under `Setups` that no setup holds, that is its position in the scene. A track span is its track locations: Focus lands midway between them, and is refused with its reason when neither resolves. A starter placement marker is its track point, not its object position; see [starter placement markers](#starter-placement-markers). Focus keeps the inspected selection, works while the inventory is stale, and does not make a scene edit. If the object no longer exists, the message area reports the refusal under **Focus**. Native camera jumps might shift the floating origin or load terrain. Map coordinates remain the placement reference.

Mouse actions inside the product button or panel belong to that UI. Scrolling an observation should not also zoom the world, and dragging the scrollbar or title should not select objects behind the panel. A drag that begins inside remains captured until release. Outside the panel, normal world navigation remains available.

When the inventory hasn't been scanned or has changed, the first status lamp turns amber and reads **Stale** instead of **Current**. It also reads **Stale** when the edited mod's scene patch file changes outside scene patch editing while the native map editor runs. Saving from the native map editor doesn't make it stale. Neither does reordering or re-spacing a file's members, because the content is equal.

The lamp's tooltip, session details and log give the reason. This can be the first observation that changed or disappeared, a changed number of observations, a changed map context, or a failed check. Stale means the observations differ from the scene or the check couldn't complete. Using the panel alone doesn't make the inventory stale. You can still inspect rows and use **Focus** while it is stale. Only edits are refused. The **Detected problems** section on the **Object** tab says that its problems might be out of date: they're the problems of the last refresh.

Runtime references, such as culling or audio lists, are held in fields the game doesn't serialize. These references and enabled flags the game switches while it runs, such as a crossing bell's audio player, appear under **Runtime state** on the **Object** tab. They don't make the inventory stale.

**Dependencies** and **Runtime state** on the **Object** tab show a short form by default. Each object of the inspected hierarchy gets one line: its path relative to the inspected object, or **This object**, followed by its components. For example, `Gate: CrossingGate, MeshRenderer`. Lines about anything else come first, unchanged. Click the triangle right after a heading, or the heading itself, to switch that section to the full lines, with each component's scene, full path, and full type name. Click it again to return to the short form.

While open, the panel compares a small part of the scene with its snapshot each frame. It starts the next comparison two seconds after finishing the last one. With more objects loaded, a scene change takes longer to make the inventory stale. The panel also checks supplied scene patch files for changes every two seconds. Neither check replaces the displayed snapshot.

Before an edit, the patcher compares the whole scene again and refuses the edit if anything differs. This check is independent of the background comparison. Refresh replaces the observations and keeps the inspected object if its handle and component still exist. Refresh after native edits, Undo/Redo, activity changes, or scene patch file changes. Observations of supplied scene patch files update only when you explicitly refresh.

Focus uses the inspected object itself, not a fresh inventory, so it never jumps to another object that has the same path. Before a complete scene patch file placement, the engine repeats unique resolution and preflight immediately before mutation and checks that the map context has not changed. Scene patch editing integrations can additionally pass an inspected-target token to reject changes in observed placement, bindings, or dependencies. Refresh cannot substitute another object with the same path for a completed edit.

## Coverage and dependencies

| Family or ownership path | Inventory coverage and editing route |
| --- | --- |
| Track markers, including yard-limit and station signs | Typed markers and serialized track-relative placement. The object's label does not establish its marker type. Generic markers that match the supported pattern carry the structure `track-marker-v1` and accept binding edits, as described in [repair, replace, or detach a track-marker binding](repairing-track-bindings.md); the others show the reason in their problems. A marker that holds a duplicate marker ID starts its dependencies with that marker ID, followed by every holder with its activity and the registration it has under the marker ID; see [markers with a duplicate marker ID](repairing-track-bindings.md#markers-with-a-duplicate-marker-id). An active generic track marker that holds a shared marker ID also shows a detected problem, with the repair offered; crossing pads and other kinds show the explanation without a problem; [repair a duplicate marker ID](repairing-duplicate-marker-ids.md) describes the steps. |
| Mileposts | Marker binding, prefix, origin, mileage, and component dependencies. Mileposts matching the supported pattern carry the structure `milepost-v1`. You can edit their placement, activity, and binding without changing the model or mile value; see [reposition or rebind a milepost](repositioning-mileposts.md). |
| Track-group-linked visuals | Serialized `TrackRelativePosition` targets, offsets, terrain snapping, and group-driven activity settings. Construction-controlled targets retain their separate segment and target relationships. |
| Crossing pads | Markers with the curve-mesh component, including their generated-geometry dependency. Pads that match the supported pattern carry the structure `crossing-pad-v1` and accept binding edits that regenerate the pad, as described in [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md). |
| Crossing detection | Loaded spans and controller circuit roles. A missing endpoint segment differs from a valid but unintended track. Crossing controllers that match the supported pattern carry the structure `crossing-coverage-v1` and accept coverage edits, as described in [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md); spans the patcher owns appear under `CrankyAnt Scene Patcher crossing coverage`. |
| Existing crossing equipment | Each crossing mast has one entry in the **Crossing mast** family for its light object and the gate on a child object. The bell that a crossing controller creates has one entry in the **Crossing bell** family. Masts and bells that match their supported source pattern have the structure `existing-crossing-mast-v1` or `existing-crossing-bell-v1` and support fixed placement and activity changes. See [move or disable an existing crossing mast or bell](moving-crossing-equipment.md). For other masts and bells, **Supported edits** explains why editing is unavailable. Each detection span appears under its controller in the outliner. To edit detection, change the controller's crossing coverage. The presence of components does not prove that detection, light output, or audible playback works. |
| Independent boards | Loaded objects with a renderer culler and a circle map mask and no track marker. Those that match the board pattern accept fixed placement and activity, as described in [move a board with its terrain effects](moving-boards.md); the others show the reason in their problems. |
| Baked scenery groups | Scene-saved group roots below `World` with a renderer, collider, light, storage pile, or log pond at or below the root, which no other family lists. Groups without game logic use `baked-scenery-group-v1` and accept fixed placement and activity changes for the whole group, including groups that hold a scene ground mask. Each listed part is a scene object at any depth below the root, named with its group and using `baked-scenery-part-v1`; an object that holds a scene ground mask is a part as well. See [move or disable a baked scenery group](moving-baked-scenery-groups.md). Other groups are marked `inspection only` under their category, with the reason, and list no parts. |
| Spawn points | Every loaded `Character.SpawnPoint`, such as those under `World/POIs`, with its name, priority, and the start setups that name it. They carry the structure `spawn-point-v1` and accept fixed placement and activity, as described in [move player spawn points](moving-spawn-points.md). The **Object** tab names their uses in the game. Native spawns in `Map.json` stay with the native map editor. An inactive spawn point is not a problem. |
| Station map icons | Each `UI.Map.MapIcon` below `World/Map Labels/Station Icons` has one entry in the **Station map icon** family; the `Station Icons` container object has no row. Icons carry the structure `station-map-icon-v1` and accept fixed placement and activity changes; see [move or disable a station map icon](moving-station-map-icons.md). Map icons that the game creates while it runs are not listed. |
| Retained bridges and construction scenery | The original inventory's identified Ela bridge culler and separate `Game.Mow.TrackConstructionScenery` controller. The controller exposes its segment IDs, construction conditions, and referenced targets, including the bridge's steel and abutments. Its construction segments are its only edit; see [repair the construction segments of a rail bridge](repairing-construction-segments.md). Its placement and Enabled setting stay unavailable. |
| Generated track trestles | The two `AutoTrestle` controls identified near Ela belong to generated track-segment hierarchies. They remain native track geometry, separately owned from the retained bridge. |
| Industry/passenger spans and map-object anchors | OpsDTO spans and MapObjectSpawner anchors use native map/operations data. This covers the original P7ec/Pfaj, Wilmot, and Sox anchor cases. The current Ela passenger span is native operations data; the historical FUSE warning concerns the previous game line. |
| Player-created signs | Use the native sign editor. Its ownership and persistence do not establish an inherited scene-sign editing route. |
| Missing object configurations or industry definitions | The original Sylva diesel-stand and Bryson textile cases concern native configuration/definition data, with no identified scene-object repair. |
| Signals, CTC, and interlocking | Their relationships appeared in the historical inventory but the first-version operation boundary excludes their edits. A span's presence in the inventory does not enable these operations. |

Discovery reads loaded objects, including inactive ones, without loading absent scenes. Dependencies include child components, serialized relationships between markers, spans, and controllers, and direct references in loaded scene components' fields and lists. Consumers that use identifiers or indirect references can remain unknown. If a field or graph is unavailable, the patcher reports an inspection limit. That limit doesn't establish whether a binding is valid. Names and proximity never create a functional relationship.

All newly inventoried patterns are inspection-only unless an operation adapter explicitly supports them. The table accounts for the applicable families and ownership cases in the original inventory. It does not promise discovery of arbitrary custom scene hierarchies, require a separate bridge model whitelist, or establish native rendering or gameplay behavior. An unidentified timber object in an earlier bridge image remains unattributed; the two sampled native trestles do not identify that object.

The [candidate ownership findings](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/20#issuecomment-6717432391) distinguish retained signs from native passenger and anchor data. The [operation investigation](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/18#issuecomment-6725711245) records dependency observations and their limits. These research results do not establish production inspection behavior.

The original [candidate families](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/4c00445b048adccb6920c238116d1f7e1e915db9/Research/Scope/CandidateFamilies.md) and [ownership audit](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/f8b3a72f00dc6556e5d37894605b0312658ad598/Research/NativeCapabilityAudit.md) identify the retained bridge family and its native-data distinctions. The later [bridge trial](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/5e99c72dee551762316c0b8cf5aafec5bf40b748/Research/SceneOperationBatch/Evidence/Boot02/Bridge/Report.md) identifies the construction controller's steel and abutment references. The [trestle observations](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/5e99c72dee551762316c0b8cf5aafec5bf40b748/Research/SceneOperationBatch/Evidence/Boot03/Bridge/Report.md) distinguish the sampled track-owned controls from the retained root and the unattributed timber image.
