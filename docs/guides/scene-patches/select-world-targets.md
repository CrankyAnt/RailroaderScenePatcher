<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Select targets in the world

Use world markings to select a supported inventory object for inspection or choose a target for an edit. You can select inactive objects too.

## Select an object for inspection

1. Open **Scene patches** in the native map editor.
2. Click **Select in world** in the command band.
3. Click an object's square marker, object frame or identity label to make it the candidate.
4. Check the candidate, then click **Confirm** in the footer to inspect it.

Each square marker is at its object's position. You can click the marker's `+`. If you click inside object frames without hitting a `+`, the patcher chooses the object whose `+` is nearest on screen. The candidate is blue, with a more saturated blue under the pointer. After you click, it stays plain blue. Confirmation turns its object frame and square marker gold. For an editable object, **Numeric** mode on the **Placement** tab shows its inspected placement.

The footer's first message slot, **World choice**, tells you what to choose, for example `Choose an object in the world.` The native map editor can still respond to world clicks while **Select in world** is open.

After you click a candidate, the footer shows it on a blue `Object <n>: …` line and enables **Details**. Click **Details** to show its scene, path, component and any binding identifiers below that line. Click it again to hide them.

## Choose between overlapping markings

A numbered choice list opens beside the pointer when two or more `+` markers are closer than half a marker and a click cannot distinguish them. The footer says `N objects. Select a row from the list next to the pointer.` Each row starts with the name that distinguishes its candidate. Select a row to make that object the candidate.

The row under the pointer has a blue background. Hover over a row or the candidate's `+` to see its blue world marking. A track shows a ribbon; an object shows a box. Press **Escape** to close the choice list and leave the world choice open so you can click elsewhere. You can navigate the world while the list is open.

## Read the world markings

Markings use the same colors as the panel:

- Blue marks the candidate. Its label starts with `Candidate: …` when the choice list is closed.
- Gold marks the inspected object. It has an object frame and a square marker at the center, with no label to obstruct its gizmo. The panel shows any detected problem.
- Green marks a confirmed target until you use or withdraw it. Its world label starts with `Confirmed choice: …` and appears while you inspect the object it was chosen for. While this target is green, the marking for the track the object is currently bound to or follows is hidden. That marking returns when you use or withdraw the target.

Labels have opaque backgrounds and keep the same text size on screen. Markings and labels stay outside native windows and controls. Candidates at the same spot appear as numbered rows in the choice list beside the pointer.

An object shows its object frame within 500 meters of the camera. Beyond that distance, it shows only its square marker and any label, in the color its object frame would have. You can still click the marker. The inspected object keeps its gold object frame and square marker at every distance. An object with nothing of its own to frame, such as a part that is switched off, still has a small object frame around its `+` within 500 meters.

Labels are centered below their markers. If a label would cover another marker or a native panel, it moves to the side or above. Inactive objects remain selectable through their square markers and identity labels. During a replacement choice, if the inspected object also has a candidate label, that label starts with `Inspected: …`.

### What an object frame includes

The patcher uses the object's renderers to determine its bounds. It leaves out effects such as smoke, water spray and lamp beams, invisible shadow casters, and lower levels of detail. A renderer hidden by the game's distance culling still counts. A static-batched building part counts once it has appeared with the patcher's markings since the map loaded. This keeps the object frame's size and shape stable as you zoom or the game culls the object. A renderer or object that is switched off does not count.

The square marker sits at the middle of the framed box, at a point on the object. It stays on the object as you zoom and turn the camera, also when that point is outside the view, as for an object larger than the view. A crossing pad is framed along its whole length on the track, including where its ends meet the road.

An object frame includes the part at the square marker and nearby parts. It leaves out a part that is far away or much larger. A baked scenery group, such as a station building with its platform and lamps, counts as one object and has an object frame around all its parts. An object with several parts has one object frame around them.

The object frame is the on-screen outline of a box that turns with the object. It follows an object angled to the view rather than enclosing it in a screen-aligned rectangle. Clicking beside the object, inside that rectangle but outside the object frame, does not select it.

Bridge construction scenery is framed around the objects it shows and hides that have no inventory entry of their own. If there are none, it has a small object frame around its own `+`. It therefore never uses the bridge's object frame. Close up, the object frame remains visible even when the object fills the view. Every object frame is at least slightly larger than its square marker, so a small or distant object still has a visible outline around its `+`.

The box stops where the object, including its wider parts, meets the terrain or ballast. It remains a single box:

- The buried foot of a post is outside the object frame.
- A part that extends into a bank is framed up to the bank.
- On sloping ground, the bottom of the box follows the lowest ground beneath the part, so a heap on a slope keeps a box-shaped object frame.
- A bridge over a valley keeps its whole box. The track on its deck and the water beneath it do not count as ground.

## Select objects in a group

A group contains scene objects that the game handles together. Some follow a shared root; others follow the objects named by a game component:

- A grade-crossing setup contains its controller and the inventory objects below its root, such as crossing masts, the bell, detection spans and crossing pads. Each mast is one inventory entry for its light object and the gate on a child object.
- A baked scenery group, such as Bryson Coal Dealer, contains its group root and the inventory objects below it. These include its parts and objects of other families, such as the signs of a station building.
- A rail bridge construction contains its construction controller and the objects whose visibility it controls. At the Ela bridge, the controller `Ela Bridge Scenery` controls the steel and abutments of `Ela Bridge`, so the two form one group.
- An industry storage display contains the storage piles or log pond filled by one industry's storage. An industry building is a separate group and contains no storage pile.

A folder is not a group. For example, Bryson Yard is a folder, and each of its 13 yard lamp posts is a separate group. Objects do not form a group just because they are close together. An object outside every group appears on its own. Whistle signs remain separate objects at every distance.

Grouping changes only how objects appear in the world. Each object keeps its inventory entry, outliner row and edits. A grade-crossing setup's controller manages its invisible detection spans. The spans do not appear in the group's object frame or choice list. They remain in the outliner under their controller in the **Crossing controller** family. To change detection, use the controller's **Connections** tab.

### Select at different distances

The patcher measures the distance from the camera to the bounds of a group's visible objects. A grade-crossing setup's detection spans do not count.

At 125 meters or less, each object has its own square marker and object frame and can be selected individually. A baked scenery group with parts shows only its parts at this distance. To inspect the group, click **Select group** on a part's **Object** tab or select the group's outliner row.

Beyond 125 meters, only the group appears, with one square marker and one object frame around its visible objects. Its square marker contains a small hollow square rather than the `+` used for an object or part. Its object frame is one box, turned with the crossing or group root and cut off at the ground like an object's frame. The square marker stays at the center of this box as you zoom and turn the camera. The object frame appears within 500 meters; beyond that, only the square marker remains. A setup with just one inventory object appears as that object.

Beyond 125 meters, click the group's square marker or object frame to open a choice list beside the pointer. The object frame turns blue wherever a click would select the group. The list contains the group's objects, excluding a grade-crossing setup's detection spans. If the group's square marker overlaps other markers at the pointer, those objects appear in the same list.

When you select a single grade-crossing setup and the list contains only its objects, the footer says `N objects in this crossing setup. Select a row from the list next to the pointer.`

Select a row to make that object the candidate, then click **Confirm** to inspect it. The candidate has its own blue square marker and object frame. The inspected object has its own gold square marker and object frame. The rest of the group keeps its shared marking. Opening, using or closing the choice list changes no scene objects, scene patch data or completed edits. You can still navigate the world while it is open.

These rules apply to every scene-object choice. If a replacement choice offers only some objects in a group, the group still appears, but its choice list contains only those objects. If a choice offers only a setup's detection spans, the spans appear individually and no group appears.

### Find a baked scenery group in the outliner

The **Baked scenery group** family groups its rows under these categories: Station building, Platform, Lights, Sign, Grade crossing, Bridge, Industry building, Storage display, Railroad service building and Other. Bridge has the subgroups Rail bridges and Road bridges. Industry building contains buildings where production uses inputs and produces outputs. Other contains listed groups that fit no other category. Every road bridge appears under Road bridges, including the road bridges below the `World/Roads` folders. The bridge and the construction controller of a rail bridge construction, such as `Ela Bridge` and `Ela Bridge Scenery`, appear under Rail bridges as rows of their own.

Each group appears under its category. Its row shows the group's name followed by its location, for example `Alarka Jct / Crossing Unprotected (1)`. Parts appear beneath their group, never as separate rows outside it. If a search or filter matches only a part, the outliner shows it beneath a dimmed group row. The dimming means that the group itself does not match: a group does not match a search through the names of the objects below it. A group with no supported edit remains under its category, marked `inspection only`.

When you inspect a part, the **Object** tab starts with `Part of:` and the group's name. Click **Select group** beside this line to inspect the group. The button appears only for a part.

## Confirm a target for an edit

On the inspected object's **Connections** tab, click **Object**, **Segment** or **Node**. For crossing coverage, open a circuit or detection addition and click **Segment** beside the role you want to change. The source object keeps its gold object frame to show which object the choice belongs to.

For a segment, click the track in the world. For a node, click its marking. Under **World choice**, the footer shows the candidate in blue as `Candidate: …` or `Candidate <n>: …` if it has a number in the choice list. **Details** shows the segment ID and both endpoint IDs, or the node ID and map position.

Hover over a track to see a selectable ribbon along its curve. The ribbon covers the rails and ties and keeps a minimum width on screen at a distance. Clicking a shared endpoint or overlapping ribbons offers every matching candidate so you can choose explicitly.

During a world choice, every candidate of that type in view has a `+` marker even when the pointer is elsewhere. A track has this marker within 500 meters of the camera. When choosing a track location, you can select another point on the current segment. A crossing role's choice excludes its current starting segment.

Green also marks the current binding, every segment covered by the crossing and the track the object follows. The current binding appears while the **Connections** tab is open, whether it comes from the map or a scene patch. A confirmed target temporarily hides the current track marking as described above.

World choices for replacements, snaps and track-following placement use the same candidate markings and choice lists. **Confirm** turns the candidate into a confirmed target, marked green. It does not inspect the candidate as **Select in world** does.

Use **Focus** in the footer to check the location. You can navigate outside the panel and inspect other inventory entries while the choice stays open for its original source object.

After you check the candidate, click **Confirm**. The patcher records the confirmed identity in the loaded map context. While you inspect the object the target was chosen for, the footer's first message slot, **World choice**, shows a green `Confirmed choice: …` line. Inspecting another inventory entry ends that confirmation, also when it belongs to the same object. A choice made through **Select in world** alone has no source object and does not show that line.

If the candidate is missing, ambiguous or has changed since you selected it, check the world and select it again. If the source object or loaded map context has changed, cancel the choice and start again.

### Resolve an ambiguous identity

A scene patch identifies an object by its scene, path and component. If two objects share all three, it cannot distinguish them. Confirmation for an edit is refused and names the shared identity. Choose an object with a unique path instead. A replacement choice excludes objects with this ambiguity because the scene patch could not identify them.

**Select in world** can still confirm such an object for inspection. **Supported edits** then explains why it cannot be edited. A refused confirmation shows its reason in the footer and the log.

Click **Cancel** in the footer to close a pending choice. A pending choice changes no scene objects, scene patch file or completed edit history. Confirmation records a choice for an edit. The [v1 scene patch file reference](scene-patch-file-v1.md) describes the supported fields and how they apply.

## Apply the edit

Confirm or cancel a pending choice before applying placement or activity changes in **Scene patches**. Completed edits use the native map editor's **Undo**, **Redo** and **Save** controls. See [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md).

For a one-time physical placement, use **Track** or **Node** in **Snap** mode on the **Placement** tab. Confirmation prepares the result; **Apply** makes the edit. See [snap an object's position to a track](snapping-to-track.md).

Editing actions on the **Placement** and **Connections** tabs pause native map editor world clicks, including right-click. You can still rotate and pan the camera with the right and middle mouse buttons, use **Scene patches**, make world choices and use gizmo handles.

The block ends when the corresponding action ends through **Apply**, **Discard**, **Start**, **Set**, **Confirm** or **Cancel**. **Read** also ends it, as does leaving a **Numeric** field without a change. In **Gizmo** mode, the block ends when you switch mode or tab, clear the inspection or close the window.
