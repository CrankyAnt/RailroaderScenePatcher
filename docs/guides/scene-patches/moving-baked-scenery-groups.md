<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a baked scenery group

Use the patcher to move, rotate, disable or restore baked scenery groups and their parts. These groups include platforms, lamps, signs and small buildings that the native map editor does not edit. A part might be a sawmill jetty or a station lamp.

When you move a group, its physical placement changes and everything below its group root moves with it, including its colliders and culling. Each part keeps its activity. When you edit a part, only that part and the objects below it are affected. The group root and other parts stay where they are. Moving or disabling a group or part changes no game function, unless a section below says otherwise.

A baked scenery group is saved in the map's scene. Props, masks and other objects in the map's `Map.json` are native map data, which you edit in the native map editor. Editing a baked scenery group requires patcher 0.5.0 or later. When you click **Save** in the native map editor, the patcher derives the dependency minimum from the scene patch file's content. See [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## Which groups are listed

The **Baked scenery group** family in the outliner lists one entry per group root, under the group's category. See [selecting in the world](select-world-targets.md). A group root is a scene object one level below a `World` folder, such as `World/Roads Barkers/1932 Chevy Truck LP`. Under `World/Large Scenery`, the root is one level below a town folder, such as `World/Large Scenery/Bryson/Bryson House Track`. The `World/Large Scenery/Almond Bridge` folder is itself a group root, because it holds the construction of one rail bridge; see [rail bridges outside Ela](#rail-bridges-outside-ela).

To be listed, a group must be saved in the scene, have a renderer, collider, light, storage pile or log pond at or below its root, and belong to no other family. The following objects have no group entry:

- Objects without a renderer, collider, light, storage pile or log pond. A spawn point under `World/POIs` has its own **Spawn point** family; see [move player spawn points](moving-spawn-points.md).
- Visual effects, such as the fireflies under `World/Environment Effects`.
- Folders, such as `World/Large Scenery/Bryson/Bryson Yard`. Each of the 13 yard lamp posts below that folder is its own group.
- `Almond/Cube` and `Bryson/Bryson Appalachian Hardwoods`, which have no model or game function.

A group contains only what the scene saves below its root. Props, tanks, conveyors and terrain masks in the map's `Map.json` may stand beside a group, but they are native map data. The native map editor moves them; moving the group leaves them in place. The coal and oil tanks beside the lamp post `Bryson Oil` and the conveyor beside the coal pile `Coal & Oil` at Andrews are such Props.

Groups that contain game logic or shape the terrain need separate support. They are marked `inspection only`, and the **Object** tab explains why. This includes groups containing a station click spot, scene ground mask or map icon. The same restriction applies to road bridges and crossbucks at unprotected crossings.

Groups containing track markers or spawn points are also marked `inspection only`. Such a group is not moved as a whole. Instead, you edit each of its markers in the **Track marker** family and each of its spawn points in the **Spawn point** family. If a category names a group marked `inspection only`, it stays under that category in the **Baked scenery group** family. Otherwise, it appears in the **Inspection only** group at the end of the outliner. A group marked `inspection only` lists no parts.

## Which parts are listed

Each group in the **Baked scenery group** family lists its parts below its own row, sorted by name. A part's row shows the path from the group root, such as `Jetty / Piece (2)`. Its tooltip and searches use the full name, including the group, such as `Whittier Saw Mill / Jetty / Piece (2)`.

A part is a scene object at any depth below the root, rather than a renderer or component. To be listed, it must be saved in the scene and have a renderer, collider, light, storage pile or log pond at or below it. A part can contain other parts, which move with it.

Some objects below a group root are not listed as parts:

- Sibling objects with the same name share one path. A scene patch file cannot tell them apart, so neither they nor the objects below them are listed.
- A level of a LOD group, such as `LOD0` and `LOD1` below a lamp, belongs to the entry of the object that holds the LOD group.
- A culling container, such as `Bryson House Track / Culled`, only groups parts for the game's culling. It has no row of its own and appears in no world choice list, but the parts in it are listed under their group, with the container in their name. A group whose root carries the culler, such as Sylva Culled, keeps its group row and lists its models as parts. A modification whose path names a culling container fails with that reason; the file's other modifications still apply.
- Objects the game moves during play, such as a clock hand at Bryson depot or the flame of a lantern, which the game turns toward the camera, are not listed as parts. Neither are the objects below them. These objects remain in their group and move with it.
- Objects without a renderer, collider, light, storage pile or log pond, objects created while the game runs and objects listed by another family have no part entry.

## Move a group in the native map editor

1. Open **Scene patches** and click **Refresh** in the command band. Search for the group, for example `Bryson House Track`, and select its row under **Baked scenery group**. You can also use **Select in world** to select it by its marking, as described in [select world targets](select-world-targets.md). On the **Object** tab, **Identity** shows the component `UnityEngine.Transform` and the structure `baked-scenery-group-v1`. **Use in the game** identifies the group as decorative.
2. Click **Focus** in the command band to see the group.
3. On the **Placement** tab, change the six **Numeric** fields for position in map coordinates and rotation in degrees. Click **Apply** at the bottom of the tab. You can also use **Gizmo** or **Snap**, as for other objects. See [using transform gizmos](using-transform-gizmos.md) and [snapping to track](snapping-to-track.md).

The group root moves at once with everything below it. Save, Undo, Redo and **Remove modification** in the footer work as described in [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). Removal returns the group to the placement the loaded map gives it, together with the values other mods still set.

If **Supported edits** on the **Object** tab starts with **Not editable:**, the reason follows, for example a hierarchy path that two groups share.

## Disable or enable a group

Click **Disable** at the bottom of the **Object** tab. This switches off the group root, hides the group and stops its colliders from colliding. Parts keep their own **Enabled** settings, so **Enable**, Undo or removal restores the same group.

If the root has a simple culler, as at Bryson House Track, the culler stops switching it while the group is disabled. The group stays off even when the camera comes near. When you enable the group again, the culler resumes its work. The group appears only when the culler considers it in view, as before.

## Move or disable a single part

1. Select the part's row under **Baked scenery group**, for example `Bryson House Track / Ramp`. On the **Object** tab, **Identity** shows the component `UnityEngine.Transform` and the structure `baked-scenery-part-v1`. **Use in the game** identifies the part as decorative.
2. On the **Placement** tab, change the **Numeric** fields and click **Apply**, or use **Gizmo** or **Snap** as for other objects. To switch the part off, click **Disable** at the bottom of the **Object** tab.

A part with a fixed placement keeps its map coordinates when its group moves later, including during a **Gizmo** drag. This applies whether the group move is in the same scene patch file or another mod's, regardless of key or load order. The group placement applies first, followed by each fixed part placement. A part without a fixed placement moves with its group.

Click **Remove modification** to restore a part's unmodified placement relative to its parent, which may be the group root or another part. The parent stays at its current placement, and the part moves with it again. A part below a moved part keeps its place relative to that moved part when you disable and enable it or remove its own modification.

**Disable** is unavailable for a part that a game culler switches on and off by camera distance; the editor shows the reason. The culler would override a stored disable, so the patcher cannot maintain it. A placement edit leaves the part in the state set by the culler.

## Industry buildings and storage piles

You can move or disable an industry's decorative buildings and its storage piles to make room for extra track. The industry keeps working on its native track spans.

- **Industry building** lists decorative buildings of industries, such as `Bryson Coal Dealer`, `Whittier Saw Mill Culled` and `Mine 1` at Hewitt. On their **Object** tab, **Use in the game** says that the industry works on its native track spans, so moving or disabling the group or a part does not change loading or unloading.
- **Storage display** lists the storage piles of `Andrews Snowbird Paper`, `Sylva Paperboard` and `Connelly` as groups. Each pile is a part of its group, such as `Connelly / Log Pile L1a` or `Andrews Snowbird Paper / Pulpwood Piles / Pile (3)`. Move a pile on its own, or move the whole group with all its piles.

On a pile's **Object** tab, **Use in the game** names the storage that the pile shows, such as the pulpwood storage of its industry. Moving or disabling the pile does not change the storage. At its new place the pile still fills and empties with that storage, because the game links the pile to the storage by ID, not by position. The pile's logs, its collider and its culling move with it. A disabled pile shows nothing, and the storage stays as it is.

The industry itself is native map data. Its position, its track spans and their delivery and loading points stay with the native map editor. The patcher does not move them, and moving a building or pile never moves a track span. To change where cars load and unload, edit the industry's track spans in the native map editor.

## Lake surfaces

You can move, rotate or disable a lake surface to fit a changed river or track. A lake surface is a water mesh that the scene saves. `Fontana Lake (1)` and `Fontana Lake (2)` below `World/Rivers` are lake surfaces, each its own group under **Other**. The `Whittier Log Pond` group at the Whittier saw mill, under **Storage display**, holds a lake surface and a log pond.

The native map editor still owns the lake masks and rivers in `Map.json`. Native masks shape and color the terrain, and the water surface is the scene object. The patcher moves the lake surface and leaves the native mask where it is. To reshape the terrain under the moved water, edit the mask in the native map editor.

The `Whittier Log Pond` group has two parts: `Whittier Log Pond / Whittier Saw Mill Log Pond`, its lake surface, and `Whittier Log Pond / LogPond`, its log pond, which lays out the logs of the sawmill's storage in the water. Move the whole group to move the water with its logs, or move a part on its own.

On the **Object** tab of the `Whittier Log Pond` group and of its `LogPond` part, **Use in the game** names the storage that the log pond shows and says that moving or disabling the group or the log pond does not change the storage. The game links the log pond to the storage by ID, not by position. After a move, the patcher moves the log pond's culling sphere to its new place and has it lay out its logs again there, as it does for a storage pile.

A Fontana lake is one object without parts. After a move, the patcher moves the sphere of its renderer culler to the lake's new place.

If the game draws a lake surface from a static batch, a move would leave the water drawn at its old place. The patcher refuses such a move before it changes anything and says so; **Disable** and **Enable** still work. Under **Dependencies**, the **Object** tab of a lake surface says whether its water is part of a static batch.

## Move the coal loader with the track under it

The `Coal Loader` group at Robinson Gap Mine, under **Industry building**, carries the scene's car loader on its group root. The car loader looks for a car under the group root's position and fills it with the load that the scene sets for it. It reads no industry storage. When you move the group, the patcher moves this scene object, so the position the car loader reads moves with it. The patcher does not change the load.

The native Prop `aj-coal-loader-mine` stands beside the group, about 3 m away. It is native map data, so moving the group leaves it in place.

On the group's **Object** tab, **Use in the game** reads `Loading point: cars under it are loaded.` The group's only part is `Coal Loader / Model / Spotlight Outdoor`, and its tab says that moving or disabling it does not change loading. `Model / MineLoader`, the chute and frame that the game animates while it loads, moves with the group and has no row of its own.

Track and Props are native map data, so move them first:

1. In the native map editor, move the track under the loader and the Prop `aj-coal-loader-mine` to the new place.
1. In **Scene patches**, select `Coal Loader` under **Baked scenery group** > **Industry building** and move the group so that its group root stands above the moved track, as described in [move a group in the native map editor](#move-a-group-in-the-native-map-editor).
1. Click **Save** in the native map editor.

A car that the loader is filling when you move the group keeps loading at the old place until the car leaves; the next search uses the new position. While the group is disabled, its car loader does not run.

## Buildings with lanterns and doors

Four buildings under **Railroad service building** have parts that you click in the game:

- `Bryson Section House` has a trainman's lantern, `Bryson Section House / TrainmanLantern`, that switches on and off.
- `Bryson Tower Chance Clayton`, the tower at Bryson, has a lantern, `Bryson Tower Chance Clayton / Culling Container / TrainmanLantern`, and a name sign whose text is the part `Bryson Tower Chance Clayton / Culling Container / Sign-Station / Text`.
- `Bryson Turntable` and `Roundhouse 3` at Dillsboro are roundhouses. Each stall, such as `Bryson Turntable / Roundhouse / Stall (2)` or `Roundhouse 3 / Stall (1)`, has doors that open and close.

Move or disable these buildings and their parts as any other group or part. The game reads no position for a lantern or stall, so a moved lantern or stall should work at its new place: you click it there, and it switches there. Not yet observed in Railroader.

The game keeps the state of each lantern and stall door by its key, not by its position. The building holds the states under an ID that the game saves: the group root of `Bryson Section House` and `Roundhouse 3`, the `Roundhouse` part of `Bryson Turntable`, and the culling container of the tower. A move changes no key or ID, so the states should stay; this is not yet observed in Railroader. A disabled lantern or stall cannot be clicked. When you disable the object that holds the states, or a group or part above it, the game stops tracking that ID, so a game save made while it is disabled might not keep the states. The patcher does not refuse this, as the native map editor does not.

On the **Object** tab, **Use in the game** names each interactive part and its key:

- A lantern reads `Lantern switch: click the lantern in the game to switch it on or off.`, followed by its key and the note that its state stays when the part moves.
- A stall reads `Door toggle: click the doors in the game to open or close them.`, with the same note.
- The object that holds the states names their saved ID. For the tower, whose holder is its culling container without a row of its own, the group names it.
- A group or part above the object that holds the states, such as the `Bryson Turntable` group, says that disabling it also disables that object, so a game save might not keep the states.
- A group or part with interactive parts below it says how many it holds.
- The tower's name text says that the scene keeps the text with the part, so the text moves with it.

The object that animates a stall's doors, such as `Stall (2) / Roundhouse Modular A Stall`, has no row of its own. It holds the stall's walls, roof and doors and moves with its stall, so move the stall to move the doors. The turntable and the tracks into the stalls are native map data; move them in the native map editor.

## Rail bridges outside Ela

You can move, rotate or disable a rail bridge construction outside Ela so that it stands where you moved its track in the native map editor. Each construction is a group under **Bridge** > **Rail bridges**, beside `Ela Bridge`:

- `Bridge 1` at Bryson, with its parts `Bridge 1 / Abutments` and `Bridge 1 / Steel` and the objects below them.
- `Almond Bridge`, the folder `World/Large Scenery/Almond Bridge`, with its parts `Almond Bridge / Abutments` and `Almond Bridge / Steel`. The folder carries the bridge's construction controller and culler, so the abutments and steel are parts of one group.
- Each of the seven plate girder bridges below `World/Large Scenery/Plate Girder Bridge`, such as `Plate Girder Bridge (1)`. Each one is one object without parts.

The construction moves with its colliders and culling. After every edit, the patcher checks that each collider keeps its shape and enabled state, and that the physics bounds of each box, sphere or capsule collider follow the construction. The patcher does not compare the bounds of mesh colliders. It also checks that the generated trestles near the bridge stay unchanged. The patcher refuses a construction that holds a track component or a generated trestle.

The track stays native map data. The native map editor moves it, and the track keeps its own trestle or steel flags. A rail bridge does not follow its track: **Follow** is unavailable for a rail bridge and its parts, and a hand-written track-following placement is refused. Move the track first, and then move the construction onto it:

1. In the native map editor, move the track that the bridge carries.
1. In **Scene patches**, select the construction under **Baked scenery group** > **Bridge** > **Rail bridges** and move it onto the moved track, as described in [move a group in the native map editor](#move-a-group-in-the-native-map-editor). To move only the steel or the abutments, select that part instead.
1. Click **Save** in the native map editor.

### Construction segments

Bryson Bridge 1 and the Almond Bridge each have a construction controller. It switches parts of the bridge on and off by the construction state of its construction segments: the track segments that the controller names by ID. On the **Object** tab of the construction and of its parts, **Use in the game** names these segments. The controller finds the segments by ID, not by position. Whether the construction shows its state correctly at a new place has not yet been observed in Railroader. A plate girder bridge has no construction controller, and its **Use in the game** reads as decorative.

The **Connections** tab lists each construction segment under **Observed bindings**, such as `Construction segment: <segment ID>`. If you split or replace the track natively, a segment can stop existing. The tab then shows its ID followed by `no longer exists`. The patcher reports no problem for it. What the game does with such a segment has not yet been observed in Railroader. To set the construction segments to the new track, see [repair the construction segments of a rail bridge](repairing-construction-segments.md).

### Disable an object that the construction switches

You can disable the construction root, a part or a plate girder bridge. A disable holds even against the construction controller: the object stays off when the controller would switch it on. The **Object** tab and the footer's message area then show an amber warning that the object stays off in the game although the game's track construction progress switches it on, because your disable holds against it. **Enable** or **Clear activity** returns the object to the controller's current choice. Which objects of Bryson Bridge 1 and the Almond Bridge the controller switches has not yet been observed in Railroader.

A part moved outside its culler's sphere gets its own culling sphere, as described in [what the patcher checks](#what-the-patcher-checks). For a part that the controller switches, its own sphere shows the part only while the controller shows it.

## Write the modification by hand

Click **Copy ID** in the command band to copy the identity. Add a modification under a key of your choice in the `modifications` object of `Data/scene-patches.json`, as described in [scene patch file v1](scene-patch-file-v1.md):

```json
"move-bryson-house-track": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Bryson/Bryson House Track",
    "component": "UnityEngine.Transform",
    "structure": "baked-scenery-group-v1"
  },
  "placement": {
    "mode": "fixed",
    "position": [0.0, 0.0, 0.0],
    "rotation": [0.0, 0.0, 0.0, 1.0]
  },
  "dependsOn": []
}
```

Use the scene name and coordinates from the inspected identity. Add `"active": false` to disable the group.

A part modification uses the part's path and the structure `baked-scenery-part-v1`. Select the part's row and click **Copy ID** to copy its identity:

```json
"move-saw-mill-jetty": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Whittier/Whittier Saw Mill/Jetty",
    "component": "UnityEngine.Transform",
    "structure": "baked-scenery-part-v1"
  },
  "placement": {
    "mode": "fixed",
    "position": [0.0, 0.0, 0.0],
    "rotation": [0.0, 0.0, 0.0, 1.0]
  },
  "dependsOn": []
}
```

The part placement uses map coordinates, not coordinates relative to the group.

## What the patcher checks

Before changing a group, the patcher checks that its scene is loaded, its path names exactly one listed group root, and it contains no missing script or rigidbody. If the path names a folder, such as `World/Large Scenery/Bryson/Bryson Yard`, the modification fails with an explanation that the folder only holds groups. A modification also fails if its path names no group or several groups. In each case, the file's other modifications still apply.

For a part, the path must name exactly one listed part, and its group must pass the same checks. If a part modification names a group root, such as a yard lamp post below Bryson Yard, it fails with an explanation that the path names a group root. The file's other modifications still apply. Edit that object as a baked scenery group.

After changing a group, the patcher checks that the root's placement and activity match the mod values. Every part must keep its place relative to the root, except for parts with their own fixed placement, which must stay at that placement. Each part must also keep its own activity unless a culler switches it. Every enabled culler's sphere must be at the culler and keep the radius set in the scene. A disabled group has no culling sphere.

After changing a part, the patcher checks its placement and activity. The group root and every other part must keep their placement and activity. Objects below the edited part must keep their placement relative to it. A culler on a moved part moves with it.

A game culler hides its objects when its sphere leaves the camera's view. If you move a part outside the sphere of a culler above it, the patcher gives the part its own culling sphere. This sphere is centered on the part's renderers, colliders and lights and encloses them. The patcher registers it with the game's culling manager, so the part appears while its own sphere is in view, however far it is from its group.

The original culler keeps the radius set in the scene, and the group's other parts continue to use its sphere. Objects between that culler and the moved part stay on while either sphere is in view.

A part you move within the culler's sphere gets no sphere of its own. Neither does a part you have never moved. Moving a part back inside the original sphere returns it to that culler. A disabled part has no sphere of its own. Disabling a group pauses its parts' separate spheres.

A part with its own culler keeps that culler, which moves with the part. The scene patch file has no fields for these culling changes. The patcher derives the separate spheres from the placements each time it applies them.

After changing a part, the patcher also checks that every moved part outside a culler's sphere has a separate sphere at the part.

If any check finds a difference, the modification fails and the patcher restores the group or part. If it cannot verify restoration, it reports incomplete cleanup and blocks further edits to that group or part until **Recheck** succeeds or the world reloads.

## Limits

Grass and other terrain detail under a group come from the terrain and its masks. A native map mask, such as `br-house-track` under Bryson House Track, keeps the ground clear at the group's original position. After a move, grass grows through the group at its new position until you move or add the mask in the native map editor.

Automated tests check group behavior with a controlled group. Runs on the base map in Railroader 0.6.2 build 26335745 observed Bryson House Track, Bryson Oil, Coal & Oil at Andrews and the inactive Alarka/Platform render at their new positions both nearby and at 220 to 450 m. In those runs Bryson Yard was still listed as one group: it moved with all its posts and lamps, and no part remained at its old position. Bryson Yard is now a folder, and each of its lamp posts is its own group. The player character stood on the moved Bryson House Track platform and was stopped at its edge, walked freely through its old position, and encountered neither position while the group was disabled. A disabled group with a simple culler on its root stayed off when the camera came near. After Enable, it appeared only when the camera came near again. After a camera jump shifted the floating origin, a moved group kept its map position and the saved position had no origin offset.

The runs did not cover other groups, groups with game logic or terrain shaping, or multiplayer.

Lake surfaces are not yet measured in Railroader. Automated tests check their listing, edits, refusal, restoration, Undo, Redo, Save and reload with controlled groups. Not yet observed in the game: whether the water and the logs appear at the new place, whether the log pond keeps following the sawmill's storage there, whether a moved lake appears and disappears by camera distance at its new place, and whether the game draws the lake surfaces of the base map from a static batch.

Automated tests also check part edits and a moved part's separate culling sphere with a controlled group.

Rail bridges outside Ela are not yet measured in Railroader. Automated tests use a controlled construction. They check the listing, moves with colliders and culling, a disable that holds against the construction controller, own culling that follows the controller, refused track following, failure restoration, Undo, Redo, Save and reload. Not yet observed in the game: collision and culling at the new place, the native track's trestle or steel flags, construction visibility after a construction-state change, and a train crossing a moved bridge.

These results hold for the Railroader 0.6.3 journey on the Whittier saw mill, Bryson House Track, Bryson Yard and Bryson Signs groups. In that run Bryson Yard was still one group, and its lamp post Tall Post Shade was moved as a part. A moved part outside its culler's sphere got a separate culling sphere, and the culler kept its radius (300 m for the saw mill). The part showed at 64 m, 2 km and about 5 km from its culler, in view and out of view, and returned to the culler when moved back inside. Undo, Redo, **Remove modification**, disabling, Save and reload, a floating-origin shift and a cancelled gizmo gesture left one separate sphere or none, as expected. Not measured: collision at the new place, a Bryson Signs container whose own culler runs, and a grade crossing with a moved neighbor. The unedited saw mill switched off between 1175 m and 1308 m from the camera, far beyond the culler's 300 m radius.
