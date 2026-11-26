<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move or disable a group and its parts

Use the patcher to move, rotate, disable, or restore groups and their parts. These groups include platforms, lamps, signs, and small buildings that the native map editor does not edit. A part might be a sawmill jetty or a station lamp.

Moving a group changes its physical placement and moves everything below its top scene object, including colliders and culling. Each part keeps its activity. Editing a part changes only that part and the objects below it, leaving the top scene object and other parts in place. Moving or disabling a group or part doesn't change any game function except where described below.

The map's scene stores these groups. Props, masks, and other objects in the map's `Map.json` are native map data. Edit those in the native map editor. Editing a group requires patcher 0.5.0 or later. When you click **Save** in the native map editor, the patcher derives the dependency minimum from the scene patch file's content. See [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## Which groups are listed

In the outliner, each group appears once, under the group's category. See [selecting in the world](select-world-targets.md). A top scene object is a scene object directly below an object that only holds other groups under `World`, such as `World/Roads Barkers/1932 Chevy Truck LP`. Under `World/Large Scenery`, top scene objects are directly below a place's object that only holds other groups, such as `World/Large Scenery/Bryson/Bryson House Track`. An exception is `World/Large Scenery/Almond Bridge`, which is itself a top scene object because it holds the construction of one rail bridge. See [rail bridges outside Ela](#rail-bridges-outside-ela).

To be listed, a group must be saved in the scene, have a renderer, collider, light, storage pile, or log pond at or below its top scene object, and belong to no other family. The following objects have no group entry:

- Objects without a renderer, collider, light, storage pile, or log pond. A spawn point under `World/POIs` has its own **Spawn point** family; see [move player spawn points](moving-spawn-points.md).
- Visual effects, such as the fireflies under `World/Environment Effects`.
- Objects that only hold other groups, such as `World/Large Scenery/Bryson/Bryson Yard`. The patcher doesn't list them. Each of the 13 yard lamp posts below Bryson Yard is its own group.
- `Almond/Cube` and `Bryson/Bryson Appalachian Hardwoods`, which have no model or game function.

A group contains only the objects saved below its top scene object in the scene. Moving it leaves nearby native map data in place. Edit Props, tanks, conveyors, and terrain masks from `Map.json` in the native map editor. For example, the coal and oil tanks beside the lamp post `Bryson Oil` and the conveyor beside the coal pile `Coal & Oil` at Andrews are Props, so they don't move with those groups.

Groups that contain game logic need separate support. They are marked `inspection only`, and the **Object** tab explains why. The same restriction applies to groups containing a station click spot or map icon. A group that holds a scene ground mask is listed; see [buildings with ground masks](#buildings-with-ground-masks) and [road bridges](#road-bridges).

Groups containing track markers or spawn points are also marked `inspection only` and can't move as a whole. Edit their markers in the **Track marker** family and their spawn points in the **Spawn point** family. A group marked `inspection only` stays under its category in the outliner. A group that fits no category stands under **Unclassified**. These groups list no parts.

## Which parts are listed

Each group lists its parts below its own row, in alphabetical order. A part's row shows the path from the top scene object, such as `Jetty / Piece (2)`. A search matches the full name, including the group, such as `Whittier Saw Mill / Jetty / Piece (2)`.

A part is a scene object at any depth below the top scene object. Renderers and components aren't parts. To appear in the list, an object must be saved in the scene and have a renderer, collider, light, storage pile, or log pond at or below it. A saved object holding a scene ground mask also appears, even without any of those. Parts can contain other parts, which move with them.

Some objects below a top scene object are not listed as parts:

- Sibling objects with the same name share one path. A scene patch file cannot tell them apart, so neither they nor the objects below them are listed.
- A level of a LOD group, such as `LOD0` and `LOD1` below a lamp, belongs to the entry of the object that holds the LOD group.
- An object that only carries a simple culler (the game's SimpleCuller), such as `Bryson House Track / Culled`, has no row of its own and appears in no world choice list. The parts in it are listed under their group, with the culler object in their name. A group whose top scene object carries the culler, such as Sylva Culled, keeps its group row and lists its models as parts. A modification whose path names such an object fails with the message "This path names an object that only carries a simple culler, which isn't an editable part. Edit the parts it holds directly." The file's other modifications still apply.
- Objects the game moves during play, such as a clock hand at Bryson depot or the flame of a lantern, which the game turns toward the camera, are not listed as parts. Neither are the objects below them. These objects remain in their group and move with it.
- Objects without a renderer, collider, light, storage pile, or log pond, objects created while the game runs, and objects listed by another family have no part entry. The exception is an object that holds a scene ground mask, which is listed as a part.

## Move a group in the native map editor

1. Open **Scene Patcher** and click **Refresh** in the command band. Search for the group, for example `Bryson House Track`, and select its row in the outliner. You can also use **Select in world** to select it by its marking, as described in [select world targets](select-world-targets.md). On the **Object** tab, **Identity** shows the component `UnityEngine.Transform` and the structure `scene-object-v1`. **Use in the game** identifies the group as decorative.
2. Click **Focus** in the command band to see the group.
3. On the **Placement** tab, change the six **Numeric** fields for position in map coordinates and rotation in degrees. Click **Apply** at the bottom of the tab. You can also use **Gizmo** or **Snap**, as for other objects, unless a ground mask refuses the move; see [buildings with ground masks](#buildings-with-ground-masks). See [using transform gizmos](using-transform-gizmos.md) and [snapping to track](snapping-to-track.md).

The top scene object moves at once with everything below it. Save, Undo, Redo, and **Remove modification** in the footer work as described in [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). Removal returns the group to the placement the loaded map gives it, together with the values other mods still set.

If **Supported edits** on the **Object** tab lists the group's edits under **Unavailable:**, the reason on that line follows, for example a hierarchy path that two groups share.

## Disable or enable a group

Click **Disable** at the bottom of the **Object** tab. This switches off the top scene object, hides the group, and stops its colliders from colliding. Parts keep their own **Enabled** settings, so **Enable**, Undo, or removal restores the same group.

If the top scene object has a simple culler, as at Bryson House Track, the culler stops switching it while the group is disabled. The group stays off even when the camera comes near. When you enable the group again, the culler resumes its work. The group appears only when the culler considers it in view, as before.

## Move or disable a single part

1. Select the part's row in the outliner, for example `Bryson House Track / Ramp`. On the **Object** tab, **Identity** shows the component `UnityEngine.Transform` and the structure `child-object-v1`. **Use in the game** identifies the part as decorative.
2. On the **Placement** tab, change the **Numeric** fields and click **Apply**, or use **Gizmo** or **Snap** as for other objects, unless a ground mask refuses the move. To switch the part off, click **Disable** at the bottom of the **Object** tab.

A part with a fixed placement stays at its map coordinates when you later move the group, including during a **Gizmo** drag. This also applies when another mod's scene patch file moves the group, regardless of key order or load order. The patcher applies the group placement first, then each part's fixed placement. Parts without fixed placements move with the group.

Click **Remove modification** to restore a part's unmodified placement relative to its parent, either the top scene object or another part. The parent stays where it is, and the part moves with it again. If a part is below another part that has moved, disabling and enabling the lower part or removing its modification preserves its placement relative to the moved part.

**Disable** is unavailable for a part that a game culler switches on and off by camera distance; the editor shows the reason. The culler would override a stored disable, so the patcher cannot maintain it. A placement edit leaves the part in the state set by the culler.

## Industry buildings and storage piles

You can move or disable an industry's decorative buildings and its storage piles to make room for extra track. The industry keeps working on its native track spans. In Railroader 0.6.3, Connelly still loaded on its original spans with a storage pile moved and with both its building and storage groups disabled.

- **Industry building** lists decorative buildings of industries, such as `Bryson Coal Dealer`, `Whittier Saw Mill Culled`, and `Mine 1` at Hewitt. On their **Object** tab, **Use in the game** says that the industry works on its native track spans, so moving or disabling the group or a part does not change loading or unloading.
- **Storage display** lists the storage piles of `Andrews Snowbird Paper`, `Sylva Paperboard`, and `Connelly` as groups. Each pile is a part of its group, such as `Connelly / Log Pile L1a` or `Andrews Snowbird Paper / Pulpwood Piles / Pile (3)`. Move a pile on its own, or move the whole group with all its piles.

On a pile's **Object** tab, **Use in the game** names the storage it shows, such as the industry's pulpwood storage. Moving or disabling the pile leaves that storage unchanged. The game links the pile to storage by ID, so it still fills and empties at its new position. Its logs, collider, and culling move with it. Disabling the pile hides it.

The industry itself is native map data. Its position, its track spans, and their delivery and loading points stay with the native map editor. The patcher does not move them, and moving a building or pile never moves a track span. To change where cars load and unload, edit the industry's track spans in the native map editor.

The `Coal Loader` at Robinson Gap Mine is a decorative group under **Industry building**, and you move or disable it like any other group. The game loads Robinson Gap coal by teleport between the industry's native track spans, not at the loader, so moving or disabling the loader doesn't change where loading happens. On its **Object** tab, **Use in the game** shows the same decorative text as the other decorative groups.

## Lake surfaces

You can move, rotate, or disable a lake surface to fit a changed river or track. A lake surface is a water mesh that the scene saves. `Fontana Lake (1)` and `Fontana Lake (2)` below `World/Rivers` are lake surfaces, each its own group under **Water**. The `Whittier Log Pond` group at the Whittier saw mill, under **Storage display**, holds a lake surface and a log pond.

Edit lake masks and rivers from `Map.json` in the native map editor. Native masks shape and color the terrain. The lake surface is a separate scene object, so moving it with the patcher leaves the native mask in place. To reshape the terrain under the moved water, edit the mask in the native map editor.

The `Whittier Log Pond` group has two parts: `Whittier Log Pond / Whittier Saw Mill Log Pond`, its lake surface, and `Whittier Log Pond / LogPond`, its log pond, which lays out the logs of the sawmill's storage in the water. Move the whole group to move the water with its logs, or move a part on its own.

On the **Object** tab of the `Whittier Log Pond` group and its `LogPond` part, **Use in the game** names the storage the log pond shows. It also explains that moving or disabling the group or log pond leaves the storage unchanged. The game links the log pond to storage by ID, independently of its position. After a move, the patcher moves the log pond's culling sphere and asks it to lay out its logs at the new position, as it does for a storage pile.

A Fontana lake is one object without parts. After a move, the patcher moves the sphere of its renderer culler to the lake's new place.

If the game draws a lake surface from a static batch, moving it would leave the water visible at its old position. The patcher refuses the move before changing anything and explains why. **Disable** and **Enable** still work. Under **Dependencies**, the lake surface's **Object** tab says whether its water is part of a static batch.

## Buildings with ground masks

A scene ground mask is a component that registers a shape in the game's terrain system where its object stands. The shape sets the ground and the vegetation there. The flag-stop stations `Alarka Jct Station`, `Cochran flagstopstation`, `Ela flagstopstation`, `Nantahala Station`, `Rhodo Station`, `Topton Station`, and `Wilmot Station`, and the depots `Alarka Depot`, `Almond Depot`, `Sylva Depot`, and `Southern Combination Depot` at Whittier, are groups under **Station building** with **Group** as their family.

In each of them, the mask is a `RectangleMapMask` on a part named `Mask`. The patcher lists `Mask` as a part although it has no renderer, collider, or light, because it is the part that holds the mask. On its **Object** tab, **Dependencies** explains that the mask flattens the ground and moves with this part, along with its effects on the terrain. The top scene object and any part above `Mask` name that part under **Dependencies** too, and their **Use in the game** says that the ground mask of the part `Mask` shapes the ground and the vegetation where it stands.

When a mask's holder moves, the patcher calls the game's `Rebuild` method to request one mask registration at the holder's center and rotation, followed by terrain and vegetation regeneration at the old and new positions. This behavior hasn't yet been observed in Railroader with a numeric height comparison.

As with a location sign, the patcher tracks the edited object's terrain output at the mask's center. Read the output under **Terrain** on the **Changes** tab. After an edit of the group, such as **Disable**, the **Terrain** line of `Mask` shows the group's result, for example `By the edit of its group Wilmot Station: Terrain updated: …`. Click **Retry** to request another attempt if a write stops. For a mask that crosses a terrain tile border, the patcher tracks and retries only the tile containing its center. See [the limits](#limits). Save doesn't write terrain output. If a write doesn't finish within 30 seconds, the patcher reports the completed and outstanding positions. See [move a location sign with its terrain effects](moving-location-signs.md#when-the-terrain-output-does-not-complete).

Moving a bridge, milepost, crossing pad, station map icon, or spawn point also moves any scene ground masks on that object or its children, including masks that flatten the ground. Its **Changes** tab shows the **Terrain** output. No such object holds a mask on the Railroader 0.6.3 base map.

A part with its own fixed placement keeps its mask in place when its group moves. Disabling a group or part is expected to release the mask through the component's lifecycle. Enabling it is expected to register the mask again. The patcher checks both transitions and fails the edit if the mask doesn't match the activity. This behavior hasn't yet been observed in Railroader. After **Undo**, **Redo**, restoration after a failed edit, and **Remove modification**, the patcher checks that the mask is back at its holder.

In Railroader 0.6.3, these buildings' masks also flatten the ground under them to a single height. These masks move with the objects that hold them, like any other mask. You can move the group, the `Mask` part, or a part above it with **Gizmo**, **Snap**, **Follow**, or **Numeric**. You can also enable or disable each object. The game builds each terrain tile from its base heights and the masks registered on it. After a move, it rebuilds the terrain at both positions. At the old position, it restores the ground height without the mask. At the new position, the mask flattens the ground. **Undo**, **Remove modification**, and restoration after a failed edit move the mask back and make the game rebuild the terrain at both positions again. When the world reloads, the game builds the terrain from the saved scene patch file, with the mask registered once at the object that holds it.

If the game's ground mask members are unavailable in a build, the group is refused with `The game's ground mask members are unavailable, so you can't move, enable, or disable this group or its parts.`

Hemingway flagstopstation is not a group. Its Generic track marker stands on the root of the building, so the object stays in the **Track marker** family. Moving its marker also moves its ground mask, which flattens the ground, as with the station groups. On its **Object** tab, **Dependencies** names the object that holds the mask, because the **Track marker** family has no parts. Moving any other track marker object also moves the masks on that object or its children. This includes a mask on a `Loc:` location sign that moves with its parent. When you edit the location sign itself, the **Location sign** family moves its mask.

The game's progression switches the `Cochran flagstopstation`, `Alarka Depot`, and `Alarka Jct Station` groups and the track marker on `Hemingway flagstopstation` on and off when the map loads and whenever industries change. Without a disable, the game controls this activity. If you disable an object, it stays off even when progression would switch it on. The **Object** tab and footer's message area show an amber warning explaining that your disable keeps the object off despite progression.

**Enable**, **Clear activity**, Undo, or **Remove modification** returns the object to the state progression currently sets. If progression keeps an object off and you haven't disabled it, **Enabled** reads `Yes, but currently switched off by the game`. **Disable** is unavailable if the patcher can't maintain the disable because it can't share the object's activity with progression in this load or the game's progression members are unavailable. Its tooltip explains why. In Railroader 0.6.3, the disabled Cochran station group stayed off after the Alarka Branch progression milestone and after Save & Quit and reload. See [objects the game switches](scene-patch-file-v1.md#objects-the-game-switches).

## Buildings with lanterns and doors

Four buildings under **Railroad service building** have objects that you click in the game:

- `Bryson Section House` has a trainman's lantern, `Bryson Section House / TrainmanLantern`, that switches on and off.
- `Bryson Tower Chance Clayton`, the tower at Bryson, has a lantern, `Bryson Tower Chance Clayton / Culling Container / TrainmanLantern`, and a name sign whose text is held by the part `Bryson Tower Chance Clayton / Culling Container / Sign-Station / Text`.
- `Bryson Turntable` and `Roundhouse 3` at Dillsboro are roundhouses. Each stall, such as `Bryson Turntable / Roundhouse / Stall (2)` or `Roundhouse 3 / Stall (1)`, has doors that open and close.

Move or disable these buildings and their parts as you would any other group or part. After a move, test the lantern or door by clicking it at its new position.

The game saves lantern and stall door states by key under the building's ID. Moving the object preserves those states. In Railroader 0.6.3, the Bryson Section House lantern could be switched at its new position, and the moved Bryson roundhouse doors could be opened. The lit and open states survived Save & Quit and reload. Disabling the state holder or an object above it stops the game tracking that ID. A save made while it is disabled might lose those states.

On the **Object** tab, **Use in the game** names the interactive objects and their keys for the group or part:

- A lantern reads `Lantern switch: click the lantern in the game to switch it on or off.`, followed by its key and the note that its state stays when the group or part moves.
- A stall reads `Door toggle: click the doors in the game to open or close them.`, with the same note.
- The object that holds the states names their saved ID. For the tower, whose holder is an object that only carries a simple culler and has no row of its own, the group names it.
- A group or part above the object that holds the states, such as the `Bryson Turntable` group, says that disabling it also disables that object, so a game save might not keep the states.
- A group or part with interactive objects below it says how many it holds.
- The tower's name text says that the scene keeps the text with the part, so the text moves with it.

The object that animates a stall's doors, such as `Stall (2) / Roundhouse Modular A Stall`, has no row of its own. It holds the stall's walls, roof, and doors and moves with its stall, so move the stall to move the doors. The turntable and the tracks into the stalls are native map data; move them in the native map editor.

## Rail bridges outside Ela

You can move, rotate, or disable a rail bridge outside Ela so that it stands where you moved its track in the native map editor. Each bridge is a group under **Bridge** > **Rail bridges**, beside `Ela Bridge`:

- `Bridge 1` at Bryson, with its parts `Bridge 1 / Abutments` and `Bridge 1 / Steel` and the objects below them.
- `Almond Bridge`, the top scene object at `World/Large Scenery/Almond Bridge`, with its parts `Almond Bridge / Abutments` and `Almond Bridge / Steel`. The top scene object carries the bridge's construction controller and culler, so the abutments and steel are parts of one group.
- Each of the seven plate girder bridges below `World/Large Scenery/Plate Girder Bridge`, such as `Plate Girder Bridge (1)`. Each one is one object without parts.

The bridge's colliders and culling move with it. After every edit, the patcher checks that each collider keeps its shape and enabled state. It checks that the physics bounds of box, sphere, and capsule colliders follow the bridge, but doesn't compare mesh collider bounds. It also verifies that nearby generated trestles remain unchanged. A bridge holding a track component or generated trestle is refused.

Move the track in the native map editor. It remains native map data and keeps its own trestle or steel flags. A rail bridge doesn't follow its track. **Follow** is unavailable for a rail bridge and its parts, and the patcher refuses a hand-written track-following placement. Move the track first, then move the bridge onto it:

1. In the native map editor, move the track that the bridge carries.
1. In **Scene Patcher**, select the bridge under **Bridge** > **Rail bridges** in the outliner and move it onto the moved track, as described in [move a group in the native map editor](#move-a-group-in-the-native-map-editor). To move only the steel or the abutments, select that part instead.
1. Click **Save** in the native map editor.

### Construction segments

Bryson Bridge 1 and the Almond Bridge are rail bridge constructions. Each has a construction controller. On the **Object** tab of the construction and its parts, **Use in the game** names the construction segments. The controller reads their construction state by ID. In Railroader 0.6.3, changing the construction state of a replacement segment at Bryson Bridge 1 hid and restored its bridge parts. A plate girder bridge has no construction controller, and its **Use in the game** reads as decorative.

The **Connections** tab lists each construction segment under **Observed bindings**, such as `Construction segment: <segment ID>`. If you split or replace the track natively, a segment can stop existing. The tab then shows its ID followed by `no longer exists`, and the construction's top scene object shows it as a problem, marked **!** in the outliner. In Railroader, a deleted construction segment of `Bridge 1` at Bryson left the bridge's parts visible and made the game report an error when the world loaded. The problem names the segment. Click **Repair construction segments** under **Detected problems** to open the **Connections** tab, and see [repair the construction segments of a rail bridge](repairing-construction-segments.md). The problem disappears after the repair.

### Disable an object that the construction switches

You can disable a rail bridge's top scene object or a part. If a construction controller would switch it on, it stays disabled and the **Object** tab and footer show an amber warning. For an object the controller switches, **Enable** or **Clear activity** returns it to the state the controller currently sets. In Railroader 0.6.3, disabled steel on Almond Bridge stayed off when its segment reached Complete.

A part moved outside its culler's sphere gets its own culling sphere, as described in [what the patcher checks](#what-the-patcher-checks). For a part that the controller switches, its own sphere shows the part only while the controller shows it.

## Road bridges

You can move, rotate or disable a road bridge so that it stands where you moved its road in the native map editor. Each road bridge is a group under **Bridge** > **Road bridges**, with its deck, abutments, and other objects as parts:

- `Almond Road Bridge` and `Whittier Haywood Road Bridge` below `World/Large Scenery`.
- `Concrete Road Bridge - 20m` below `World/Roads Bryson`, and `Concrete Road Bridge - 60m` under the `World/Roads Bryson/US19 Gov Island` object.
- `Dillsboro Haywood Road Bridge`, `Dillsboro Road Bridge 1`, `Dillsboro Road Bridge 2`, and `Old Home Town Road Bridge` below `World/Roads Dillsboro`.
- `Sylva Road Bridge`, `Sylva Road Bridge 20m`, and `Sylva Road Bridge 20m (1)` below `World/Roads Sylva`.
- `Concrete Road Bridge - 20m` and `Whittier Road Bridge` below `World/Roads Whittier`.
- `Wilmot Road Bridge` below `World/Roads Wilmot`.

The road remains native map data. Move it in the native map editor; moving the bridge leaves it in place. A road bridge doesn't follow track. **Follow** is unavailable for a road bridge and its parts, with the reason `Road bridges use fixed placement. They don't support track-following placement. Move the road in the native map editor, then move the bridge onto it.` The patcher also refuses a hand-written track-following placement before changing anything. Move the road first, then move the bridge onto it:

1. In the native map editor, move the road that the bridge carries.
1. In **Scene Patcher**, select the bridge under **Bridge** > **Road bridges** in the outliner and move it onto the moved road, as described in [move a group in the native map editor](#move-a-group-in-the-native-map-editor). To move only one object of the bridge, select that part instead.
1. Click **Save** in the native map editor.

The bridge moves with its colliders and its culling. Each road bridge carries a renderer culler on its top scene object; the patcher moves the culler's sphere to the bridge and keeps its radius. After every edit, the patcher checks that each collider keeps its shape and enabled state, and that the physics bounds of each box, sphere, or capsule collider follow the bridge.

A road bridge can hold a scene ground mask, a `RectangleMapMask` on its top scene object that extends the length of the bridge. In Railroader 0.6.3, the scene configures it as a tree-cutting mask, so it is expected to keep trees off the bridge. On the bridge's **Object** tab, **Dependencies** identifies the top scene object as the mask's holder. **Use in the game** describes the bridge as decorative, identifies its road as native map data, and explains the mask's effect.

When the bridge moves, the patcher asks the game to rebuild the mask with one registration at the bridge's center and rotation. It tracks terrain output at the old and new positions, as described in [buildings with ground masks](#buildings-with-ground-masks). Read the output under **Terrain** on the **Changes** tab and click **Retry** to request another attempt if a write stops. Moving a part leaves the mask on the top scene object in place. Disabling the bridge releases the mask, and enabling it registers the mask again. See [the tested road bridge behavior](#limits).

Two bridges differ:

- The mask of `Old Home Town Road Bridge` also flattens the ground. It moves with the bridge, and the game rebuilds the terrain at both positions, as with the station buildings.
- The mask component of `Whittier Road Bridge` is disabled in the scene. It holds no registration, so the bridge moves without a rebuild and without terrain output. Its **Dependencies** name no ground mask, **Use in the game** names no terrain, and the **Changes** tab shows no **Terrain** section for it.

The 60-meter bridge at US19 Gov Island stands beside the grade-crossing setup under the same `World/Roads Bryson/US19 Gov Island` object. The bridge is separate from the crossing setup. A move of the bridge leaves the crossing, its lights, gates, and bells where they are. To move the crossing's lights, gates, and bells, see [move existing crossing equipment](moving-crossing-equipment.md).

Two smoke decals at Dillsboro, `World/Roads Dillsboro/Bridge Smoke Decal Projector` and `Bridge Smoke Decal Projector (1)`, stand 9 m from the top scene object of `Dillsboro Road Bridge 1` and 4 m from the top scene object of `Old Home Town Road Bridge`, and are expected to darken those bridges above the track. They are scene objects of their own beside the bridges, not parts of them, and the patcher does not list them because a decal projector has no renderer, collider, or light. The decal keeps its position. At Dillsboro Road Bridge 1, the soot stopped appearing after the bridge moved and returned on Undo.

## Crossbucks at unprotected crossings

You can move, rotate or disable a crossbuck at an unprotected crossing so that it stands beside the road where you moved the road or track in the native map editor. Each crossbuck is its own group under **Grade crossing**, wherever its crossing stands below `World`, such as `World/Roads Andrews/Crossing Unprotected - Robbinsville/Sign-Crossing0 (1)`. In the outliner, its row reads `Sign-Crossing0 (1)` and stands under the row of its crossing, for example `Crossing Unprotected - Bryson Everett`, with the crossing's pads. Crossings of one name stand under different places, such as `Crossing Unprotected - Robbinsville` under Andrews and under Topton. To find a crossing's crossbucks, search the outliner for the crossing's name. The row's tooltip shows the full path. A crossbuck is one entry: its model and LOD levels have no rows of their own.

A crossbuck uses fixed placement and **Enabled**. Move it as described in [move a group in the native map editor](#move-a-group-in-the-native-map-editor), and disable it as described in [disable or enable a group](#disable-or-enable-a-group). Only the crossbuck changes. The crossing pad, the whistle signs, and the other crossbuck of the crossing keep their own families and stay where they are. To rebind the pad, see [rebind crossing pads](rebinding-crossing-pads.md); to move a whistle sign, see [repair track bindings](repairing-track-bindings.md).

Each crossbuck carries a renderer culler on its top scene object. The patcher moves the culler's sphere to the crossbuck and keeps its radius. Disabling the crossbuck releases its sphere, and enabling it registers the sphere again.

On the **Object** tab, **Use in the game** says that the crossbuck is decorative: the game reads nothing from it. The auto engineer sounds the horn at the crossing's pad marker, which stays in place, so moving or disabling a crossbuck changes no game function.

### Crossings that share one path

The three unprotected crossings below `World/Roads Bryson/Old River Rd` have the same name, `Crossing Unprotected`, so their crossbucks share one hierarchy path. The patcher tells them apart by the marker ID of each crossing's pad, `Mbkd`, `M0sh`, or `Mgdu`. The target keeps the hierarchy path and names the ID in its own member, `padMarkerId`:

```json
"target": {
  "scene": "<scene from the inspected identity>",
  "path": "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (1)",
  "component": "UnityEngine.Transform",
  "structure": "scene-object-v1",
  "padMarkerId": "Mbkd"
}
```

In the outliner, each of the three crossings is its own `Crossing Unprotected` row with its two crossbucks and its pad. The inspector's title shows the ID in the crossbuck's name, for example `Sign-Crossing0 (1) at Crossing Unprotected in Old River Rd (pad marker Mbkd)`. On the **Object** tab, **Identity** shows it as **Pad marker ID** below the path. **Copy ID** copies it as `padMarkerId` beside the path. Without `padMarkerId`, the path names all three crossbuck, so the modification fails before anything changes. If `padMarkerId` matches no crossing pad at that path, it identifies no crossbuck and the modification also fails. When a crossing has a unique name, its crossbuck's path alone identifies it and the target refuses `padMarkerId`. Targets that aren't crossbucks also refuse this member.

## Write the modification by hand

Click **Copy ID** in the command band to copy the identity. Add a modification under a key of your choice in the `modifications` object of `Data/scene-patches.json`, as described in [scene patch file v1](scene-patch-file-v1.md):

```json
"move-bryson-house-track": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Bryson/Bryson House Track",
    "component": "UnityEngine.Transform",
    "structure": "scene-object-v1"
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

A part modification uses the part's path and the structure `child-object-v1`. Select the part's row and click **Copy ID** to copy its identity:

```json
"move-saw-mill-jetty": {
  "target": {
    "scene": "<scene from the inspected identity>",
    "path": "World/Large Scenery/Whittier/Whittier Saw Mill/Jetty",
    "component": "UnityEngine.Transform",
    "structure": "child-object-v1"
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

Before changing a group, the patcher checks that its scene is loaded, its path names exactly one listed top scene object, and it contains no missing script or rigidbody. If the path names an object that only holds other groups, such as `World/Large Scenery/Bryson/Bryson Yard`, the modification fails with the message "This path names an object that only holds other groups, which isn't a group itself. Edit the groups it holds." A modification also fails if its path names no group or several groups. In each case, the file's other modifications still apply.

For a part, the path must name exactly one listed part, and its group must pass the same checks. If a part modification names a top scene object, such as a yard lamp post below Bryson Yard, it fails with an explanation that the path names a group and not a part. The file's other modifications still apply. Edit that object as a group.

After changing a group, the patcher checks that the top scene object's placement and activity match the mod values. Parts must keep their placement relative to the top scene object, except those with their own fixed placement, which must remain at that placement. Each part must keep its activity unless a culler switches it.

Every enabled culler's sphere must stay at the culler with the radius set in the scene. A disabled group has no culling sphere. After a move, every enabled ground mask must register at its holder's new center and direction. If a mask doesn't follow its holder, the edit fails.

After changing a part, the patcher checks its placement and activity. The top scene object and every other part must keep their placement and activity. Objects below the edited part must keep their placement relative to it. A culler on a moved part moves with it.

A game culler hides its objects when its sphere leaves the camera's view. Moving a part outside an ancestor culler's sphere gives that part a separate culling sphere. The patcher centers this sphere on the part's renderers, colliders, and lights, encloses them, and registers it with the game's culling manager. The part then appears whenever its own sphere is in view, however far it is from its group.

The original culler keeps the radius set in the scene, and the group's other parts continue to use its sphere. Objects between that culler and the moved part stay on while either sphere is in view.

A part you move within the culler's sphere gets no sphere of its own. Neither does a part you have never moved. Moving a part back inside the original sphere returns it to that culler. A disabled part has no sphere of its own. Disabling a group pauses its parts' separate spheres.

A part with its own culler keeps that culler, which moves with the part. The scene patch file has no fields for these culling changes. The patcher derives the separate spheres from the placements each time it applies them.

After changing a part, the patcher also checks that every moved part outside a culler's sphere has a separate sphere at the part.

If any check finds a difference, the modification fails and the patcher restores the group or part. If it can't verify restoration, it reports incomplete cleanup and blocks further edits to that group or part until **Recheck** succeeds or the world reloads.

## Limits

Grass and other terrain detail under a group come from the terrain and its masks. A native map mask, such as `br-house-track` under Bryson House Track, keeps the ground clear at the group's original position. After a move, grass grows through the group at its new position until you move or add the mask in the native map editor. A scene ground mask is different: the scene saves it, and the patcher moves it with its holder, as described in [buildings with ground masks](#buildings-with-ground-masks). Native masks in `Map.json` stay where they are.

Automated tests check group behavior with a controlled group. Runs on the base map in Railroader 0.6.2 build 26335745 observed Bryson House Track, Bryson Oil, Coal & Oil at Andrews, and the inactive Alarka/Platform render at their new positions both nearby and at 220 to 450 m. In those runs Bryson Yard was still listed as one group: it moved with all its posts and lamps, and no part remained at its old position. Bryson Yard is now an object that only holds other groups, and each of its lamp posts is its own group. The player character stood on the moved Bryson House Track platform and was stopped at its edge, walked freely through its old position, and encountered neither position while the group was disabled. A disabled group with a simple culler on its top scene object stayed off when the camera came near. After Enable, it appeared only when the camera came near again. After a camera jump shifted the floating origin, a moved group kept its map position and the saved position had no origin offset.

In Railroader 0.6.3, moving a crossbuck at Old River Rd changed only the one named by its pad marker ID. Undo, Redo, disabling, removal, Save and reload kept that identity. At Nantahala, the culling sphere followed the moved crossbuck and returned on removal. The crossbuck was visible at 500 m and culled by 1 km. Multiplayer remains untested.

Automated tests use controlled terrain to check moves, terrain output, and restoration for masks that flatten the ground. In Railroader 0.6.3, a test build that moved Wilmot Station with its mask showed flat ground at the new position and vegetation back at the old position, judged by eye. Ground heights before and after a move, after **Undo**, and after removal have not been compared numerically in Railroader.

A mask whose shape crosses a terrain tile border is tracked only on the tile of its center. A depot mask is 10.2 m by 26.9 m, with up to 16 m of falloff, so its footprint can cross such a border. **Retry** asks the game again only for the tile of the center, and the terrain output on any other tile of the shape is not tracked. Location signs behave the same way.

Without the game's ground mask members, the groups that hold a mask are refused, but the Hemingway marker is not refused and would move without its mask. This is a limit of this version.

A run on the base map in Railroader 0.6.3 build 26740326, with the patcher at commit `ec871d4`, observed the lake surfaces. The game draws none of the base map's lake surfaces from a static batch, so each can be moved. The water followed a moved `Whittier Log Pond` group and a moved `Fontana Lake (1)`, and nothing stayed at the old place. The logs of the moved log pond stood at its new place and followed the sawmill's log storage there. The moved Fontana lake appeared and disappeared by camera distance around its new place, as the unedited `Fontana Lake (2)` does around its own. Undo, Redo, **Disable**, **Enable**, Save, and a world reload gave one water surface and one set of logs each. Automated tests check the listing, edits, refusal, restoration, Undo, Redo, Save and reload with controlled groups.

Automated tests also check part edits and a moved part's separate culling sphere with a controlled group.

In Railroader 0.6.3, moving the US19 Gov Island road bridge moved its tree-cutting mask. Trees disappeared at the new position and returned when the bridge was disabled. The original position was clear before the move. The bridge rendered at the sampled distances and the crossing gates still worked. The run did not establish collision on the moved deck or the behavior of the crossing lights. At Dillsboro Road Bridge 1, the soot disappeared when there was no bridge above the track and reappeared on Undo. The patcher cannot move the soot decals. A road bridge mask can cross a terrain tile border. **Retry** requests work on the tile that holds the mask's center, so **Changes** can report completion while a neighboring tile is still rebuilding.

In Railroader 0.6.3, the culling sphere followed Bryson Bridge 1 when the bridge and its track moved. A train crossed it at 12 mph. The run did not measure collider bounds. At Almond Bridge, explicitly disabled steel stayed off when construction completed.

These results hold for the Railroader 0.6.3 journey on the Whittier saw mill, Bryson House Track, Bryson Yard, and Bryson Signs groups. In that run Bryson Yard was still one group, and its lamp post Tall Post Shade was moved as a part. A moved part outside its culler's sphere got a separate culling sphere, and the culler kept its radius (300 m for the saw mill). The part showed at 64 m, 2 km, and about 5 km from its culler, in view and out of view, and returned to the culler when moved back inside. Undo, Redo, **Remove modification**, disabling, Save and reload, a floating-origin shift, and a canceled gizmo gesture left one separate sphere or none, as expected. Not measured: collision at the new place, a Bryson Signs object whose own culler runs, and a grade crossing with a moved neighbor. The unedited saw mill switched off between 1175 m and 1308 m from the camera, far beyond the culler's 300 m radius.
