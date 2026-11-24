<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Railroader Scene Patcher

This context covers scene patches supplied by mods and made through scene patch editing.

## Language

### Railroader terminology

These terms retain the meanings in Railroader's [asset-pack guide](https://docs.railroadergame.com/modding/asset-packs/), [map-modding guide](https://docs.railroadergame.com/modding/map-modding/) and [load-order reference](https://docs.railroadergame.com/modding/load-order/).

**Mod**:
An addition to Railroader that a player enables in the game's mod list, for a new game or an existing save, and can disable again, except the map-adding mod that supplies a save's map. Its package holds definitions, data, scripts or bundled assets; the game's code calls it a pack.
_Avoid_: Asset pack, MapMod

**Map definition**:
The game's identified definition of a map, which is either selectable or a map patch (see Map patch). A definition and the mod that contains it are distinct.

**Map patch**:
A map definition that the game marks as a map patch, which changes its patch target inside a save and holds only the differences from it. It applies while its mod is enabled, on a save of its patch target or of a map that has the patch target among its map layers, and is not selectable.
_Avoid_: Patch, patch map

**Patch target**:
The map a map patch changes, the last entry of its map layers, which must be a map and not another map patch; the game's docs also call it the target or target map. A scene patch file's scene target map can differ, for example the bottom map layer when the patch target is a layered map.

**Map layer**:
A map definition referenced by another map definition as inherited map content, which is source context and not another active edit target. A map patch names its patch target and any map patches it builds on in its map layers.
_Avoid_: Native map layer

**Bottom map layer**:
The lowest of all layers that the game composes for a map definition, including the layers of its map layers. A map definition without map layers is its own bottom map layer; a map patch's is the bottom map layer of its patch target.

**Terrain tile set**:
A named set of terrain tiles that forms a map's ground. A map definition that is not a map patch names every set it needs, including those of its map layers, and can use a set that another map definition brings.
_Avoid_: Tile set, terrain source

**Map**:
The map definition a player chooses when starting a new game. The save keeps that map. In running text, "map" is the ordinary word unless it means a save's map.

**Base-game map**:
The map definition that Railroader itself supplies (`rr/bushnell-whittier`), with the game's own terrain tile set, shown in the New Game list as Railroader Base Map. A map patch can name it as its patch target, and most map-changing mods apply to it.
_Avoid_: Base map, base layer

**Layered map**:
A selectable map definition that builds on other map definitions through its map layers. Terrain does not inherit through map layers, so a layered map must list every terrain tile set it needs, including those of its map layers.
_Avoid_: Map edit

**Whole new map**:
A map definition that has no map layers and supplies its own terrain tile set.
_Avoid_: Total conversion

**Map-adding mod**:
A mod that supplies a selectable map definition. A mod with only a map patch is map-changing, not map-adding.

**Map-changing mod**:
A mod whose content applies to a map it does not supply, such as the base-game map, through a map patch, a map-changing script or a scene patch file. A mod can be both map-changing and map-adding.

**Map-changing script**:
A mod's script that the game runs each time it loads map data, outside the native map editor, to change that map data before the game reads it; it is the project's name for the game's `modifyMap` handler. It is one of two ways a mod's native changes reach a save on a map it does not supply, the other being a map patch.
_Avoid_: Merge script

**Scene target map**:
The map definition that a scene patch file names. A scene patch file that names a map patch is skipped in play.
_Avoid_: Target map, target when it means a map

**Native map editor**:
The game's own map editor. It edits native map data, and only a map definition of the mod being developed.

**DLL mod**:
A mod that adds code to the game itself rather than map content. The Scene Patcher is a DLL mod.
_Avoid_: Script mod

**Load order**:
The order in which Railroader loads enabled mods. For map patches, the game also orders the map patches a map patch lists above its patch target; load order decides where no listing does.
_Avoid_: Native pack load order

### Unity terminology

Unity scene retains its meaning in Unity's [scene documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/CreatingScenes.html).

**Unity scene**:
A Unity container for scene objects, including their hierarchy and components. A Unity scene is distinct from Railroader's map definition and map layers.
_Avoid_: Source scene

### Patcher terminology

These are project terms for editing and applying scene patches, not additional Railroader definition types or Unity object types.

**Scene object**:
An object in a Unity scene loaded for the map that may have physical placement, activity, logical bindings, or dependent state.

**Independent board**:
A board scene object whose physical placement is independent of any logical binding to a track.

**Terrain output**:
Content generated for terrain tiles from the current scene state, including vegetation affected by an independent board.

**Scene patch**:
Desired changes that a supplying mod makes to supported scene objects and their relationships.
_Avoid_: Patch, patch history, movement script

**Scene patch file**:
The file in a mod that expresses the mod's scene patch. It is not the map data file of a map patch.
_Avoid_: Patch, patch document, scene-patch document, patch history

**Equal content**:
Two scene patch file texts have equal content when they say the same thing: the order of keys and members, spacing, comments, the written form of a number and the order of a modification's dependencies do not count.

**Saved**:
The state of a scene patch file whose current content and the text last read or written have equal content. A scene patch file that did not exist at load is Saved while it has no modification.
_Avoid_: Clean, unchanged on disk

**Canonical order**:
The fixed order of keys and members that a Save produces when it writes a scene patch file. Reading a scene patch file never changes its order.

**Protected scene patch file**:
A scene patch file that scene patch editing will not overwrite: one that is unreadable, in a format this version does not understand, for another map, unsafe to convert, or changed outside scene patch editing since it was read. Its content is kept until it is corrected outside scene patch editing or replaced by a fresh start.

**Fresh start**:
Replacing a protected scene patch file with a new scene patch file for the edited mod, only after explicit confirmation and a backup of the original.

**Mod value**:
A value that a supplying mod's scene patch file sets for a scene object. Without it, the scene object keeps its effective underlying state.
_Avoid_: MapMod value, authored value, authored state, authored binding, authored activity

**Physical placement**:
A scene object's position and rotation in the map.
_Avoid_: Binding

**Logical binding**:
A relationship from a scene object to another object or track whose meaning is defined by the game.
_Avoid_: Placement

**Broken binding**:
A logical binding whose stored reference names something missing from the loaded map, such as a missing segment. It is distinct from a valid but incorrect logical binding.

**Track-following placement**:
Physical placement defined relative to a selected track or one of its endpoints.

**Existing target**:
A supported scene object that already exists in the map context and is identified for a scene patch.

**Modded object**:
An existing target that a supplying mod's scene patch changes. It is an existing target with a modification.
_Avoid_: Authored object

**Supported operation**:
A scene change that the patcher can edit and apply for an applicable scene object and relationship.

**Coherent operation**:
A single user-recognizable scene change with one outcome and one restoration boundary.

**Registration**:
An object's membership in a game-managed graph or other structure that makes it available to related game behavior.

**Added object**:
A scene object that a mod's scene patch adds, rather than a scene object already in the map context. Native objects that are new to a particular mod are not automatically added objects.
_Avoid_: Authored addition

**Existing crossing**:
A protected road crossing already present in the map, whose crossing coverage or equipment can be extended or corrected.
_Avoid_: New standalone crossing

**Map context**:
The context against which a scene patch's targets, tracks included, are identified: in the native map editor, the edited map definition and its map layers. In a save, the map IDs in play are the loaded map and its map layers, and an applied map patch's ID is not one of them.

**Supplying mod**:
The mod that owns a scene patch file, regardless of which map definition or mod contains an existing target.
_Avoid_: Supplying MapMod, target owner

**Edited mod**:
The supplying mod that owns the selected map definition, a map or a map patch, being edited in the native map editor. Other mods can supply map layers or terrain tile sets without becoming edit targets.
_Avoid_: Active edited MapMod

**Load attempt**:
One pass in which a player's game applies its scene patches to the loaded world. Loading the same map again starts a new load attempt, which checks each modded object and added object again by its identity.

**Retained world**:
A loaded map whose scene objects a later load attempt in the same map context may find kept or rebuilt. A kept object keeps its state; a rebuilt object receives its modification as new.

**Effective underlying state**:
The state that remains for an existing target without a particular modification by its supplying mod, including the applicable native context and other retained modifications in that editing or runtime context.

**Informational overlap notice**:
A notice that applicable scene patch files from different supplying mods attempt to modify the same resolved existing target. It indicates possible incompatibility, not a proven conflict.

**Supported source pattern**:
A supported equipment behavior with its required components, relationships and available dependent data. A model name or visual resemblance alone does not identify a pattern.

**Eligible equipment source**:
An available equipment source that conforms to a supported source pattern and satisfies its applicable requirements.

**Format version**:
The version that identifies the meaning of the data in a scene patch file. It is separate from the release versions of the patcher and supplying mod.

**Checked build**:
A Railroader build for which the patcher's relevant game functions and prerequisites have been explicitly checked.

**Track group**:
A named set of track that the map enables or makes available as a whole, for example through map features.

**Progression**:
The game's unlocking of stations and industries as play advances. A progression controller switches the scene objects of a station on or off from the station's progression state.

**Map feature**:
A part of the map that the game unlocks as a whole, such as a branch, a facility or a signal system, switching on the track groups and scene objects it names.

**Group-driven activity**:
A scene object's activity as the game sets it from whether its track group is enabled, or as the game's progression or a map feature switches that object.
_Avoid_: Group-following object

**Group subscription**:
A scene object's dependence on its track group, through which enabling or disabling the group changes the scene object's activity.

**Explicit disable**:
A mod value that holds a scene object inactive, which takes precedence over later group-driven activity.

**Circuit**:
One detection unit of an existing crossing's controller, whose detection spans, such as its approach and island spans, decide when the crossing's equipment activates.

**Detection span**:
A crossing's track span in the scene, which a circuit of that crossing uses to detect trains on the tracks in its crossing coverage. The patcher lists it under its crossing and lets the modder assign, change or detach it; approach spans and island spans are detection spans. It is either one of the map's own detection spans or an owned span.

**Crossing coverage**:
The tracks each circuit of an existing crossing detects, set per circuit by a supplying mod as omitted, explicitly selected tracks or explicitly detached. Proximity never assigns it.

**Owned span**:
A detection span that a supplying mod's coherent operation constructs and registers for selected tracks. Removal and cleanup remove owned spans; the map's own detection spans are never removed.

**Detection addition**:
A circuit that a supplying mod adds at an existing crossing: explicitly selected tracks and areas for its detection spans, with an identity scoped to the supplying mod. It adds detection without changing the crossing's native circuits. It is not an added object.
_Avoid_: Authored addition

**Modification**:
An entry in a scene patch file, identified by its modification ID, that holds the mod values a supplying mod sets for one existing target.

**Patcher dependency**:
A supplying mod's declared dependency on the patcher, with the minimum patcher version that the mod requires.

**Fixed placement**:
Physical placement fixed in map coordinates, independent of any track. It is distinct from track-following placement.

**Owned state**:
State that a supplying mod's coherent operation constructs or registers, such as an owned span or an added object. Cleanup removes only owned state.

**Refusal**:
An edit or a modification that the patcher refuses before it changes anything, with the reason stated. Nothing needs restoring after a refusal.
_Avoid_: Rejection

**Restored failure**:
An edit that failed after it began to change the scene, and whose captured prior state the patcher restored and verified. It is distinct from a refusal and from incomplete cleanup.

**Incomplete cleanup**:
A failed edit whose restoration the patcher could not verify. Its remaining state, such as an owned span that stays registered, is uncertain until the cleanup is verified or the world reloads.
_Avoid_: Unverified restoration

**Guarded retry**:
A retry of an operation whose mutation failed, blocked until the cleanup of the failed attempt is verified or the world reloads.

**Capability**:
An area of patcher behavior whose required game functions and prerequisites are checked per build, so that a demonstrated incompatibility disables only that area.

**Player log**:
The game's log file, `Player.log`, where the patcher writes its warnings and errors in both modes, and its debug lines while Debug logging is on. A row that the game's **Log** window shows reaches the Player log through the game, and the patcher adds no second line beside it.

**Debug logging**:
The option, in the **Scene Patcher** section of the **Mods** tab in Preferences, that adds debug lines to the Player log: load steps, the outcome of each scene patch file, edits, saves and diagnostics. It is off by default, takes effect at once, and is stored in the patcher's `settings.json`.

### Scene object terminology

These are project terms for the scene objects that the patcher lists and edits, not additional Railroader definition types or Unity object types.

**Inventory**:
The scene objects of the map context that the patcher lists for inspection. Rules that the coverage statement names keep other scene objects out.

**Coverage statement**:
The development reference, outside scene patch editing, that names each rule keeping scene objects out of the inventory.

**Family**:
A kind of scene object that the patcher supports under its own rules, such as track markers, scene spawn points or station map icons. An object's family decides what an edit of it may do.

**Category**:
What a scene object is to a modder, such as Station building, Platform, Lights, Sign, Grade crossing, Bridge, Industry building, Storage display, Railroad service building, Water or Road scenery. Spawn point, Starter placement, Station map icon and Track marker are categories too. An object that fits none of them is Unclassified.
_Avoid_: Other, Group

**Kind**:
What tells the objects of one category apart, such as Rail bridges and Road bridges under Bridge, or Speed signs and Mileposts under Sign.

**Place**:
A place on the map that the game names, such as Alarka Jct or Connelly Creek, and at which a scene object stands.

**Native map data**:
Map data that the native map editor edits, such as Props, native masks, Builtins, native spawns, turntables, tracks, industries, native track spans and passenger stops. The inventory lists no native map data.

**Track span**:
The game's word for a span of track, which this context keeps for every span. A span is either a native track span, in native map data, or a detection span, in the scene at an existing crossing.

**Native track span**:
A track span in native map data, which the native map editor edits, such as industry spans and passenger stop spans.

**Native spawn**:
A spawn in native map data, which the native map editor edits and calls Spawn. It is distinct from a scene spawn point.

**Baked scenery group**:
A scene-serialized group of buildings, structures, fixtures, props, lamps, signs, click spots, lake surfaces, rail bridge constructions or road bridges, which receives physical placement and activity as a whole and per part. Trees and vegetation are terrain and bulk scenery, not baked scenery. It is a group by hierarchy: one scene object and the scene objects below it. Railroader has no term for it. Compare game-defined group, whose objects are placed one by one.

**Group root**:
The scene object at the top of a baked scenery group. The inventory lists a baked scenery group as one entry for its root.

**Part**:
A scene object inside a baked scenery group, other than its group root, that can receive its own physical placement or activity. A component or mesh inside one scene object is not a part.

**Container object**:
A scene object that only holds other scene objects and has nothing visible of its own, such as the Bryson Signs object or a place's object under Large Scenery. A container object is not a group: the scene objects it holds are listed and edited by their own family or group. The native map editor's Folder is a folder of map data, not a container object.
_Avoid_: Folder

**Game culler**:
A game component that culls the objects it covers: the object it stands on, the renderers below it, or the objects on its list. Its three kinds are the simple culler, the renderer culler and the game-object culler.

**Culling sphere**:
A sphere, with a centre and a radius, that the game keeps for culling the objects it covers. A game culler holds one for its group or part, and a part that a move takes out of the sphere of a culler above it gets a culling sphere of its own.

**Culling container**:
A container object below a baked scenery group root that carries a simple culler and has no renderer, collider, light, storage pile or log pond of its own. It is not an object to edit: its parts stay listed and edited under their group.

**Scene spawn point**:
A scene object that marks a player spawn location used for starting, teleporting and rescue. It is distinct from a native spawn.

**Station map icon**:
A scene object that shows a station on the map.

**Station click spot**:
The invisible part of a station's baked scenery group that opens the station window.
_Avoid_: Click volume

**Milepost**:
A scene object that the game shows as a mile marker, with a prefix, a mile value and displayed text. A milepost with the origin flag is the origin of its prefix, and the game caches that origin.

**Industry storage display**:
The storage piles or log pond whose fill shows one industry's storage.

**Scene ground mask**:
A mask component that the game holds on a scene object and that registers a shape in the game's terrain system where the object stands, shaping the ground and the vegetation there. A native mask, which the native map editor edits, is a different thing.
_Avoid_: Scene mask, scene terrain mask

**Lake surface**:
A water mesh that the scene saves for a lake and that the game draws where its scene object stands. The lake's native mask and river are native map data.
_Avoid_: Water surface

**Log pond**:
The game's bulk scenery component for an industry's logs on water, part of an industry storage display beside its lake surface.

**Rail bridge construction**:
A rail bridge whose root holds a construction controller, which shows or hides its parts as its construction segments change. It is a game-defined group and needs its construction segments.

**Road bridge**:
A bridge for a road, a baked scenery group whose group root holds no construction controller. It is not a rail bridge construction.
_Avoid_: Road bridge construction

**Construction segment**:
A track segment that the construction controller of a rail bridge construction reads to show or hide the construction's parts. A rail bridge construction needs its construction segments.
_Avoid_: Construction scenery segment

**Construction controller**:
The game's controller on the root of a rail bridge construction. It sets the activity of the objects its entries name whenever the construction state of its segments changes.
_Avoid_: Track construction controller

**Scene marker**:
A scene object, such as a track marker, that the game registers under a marker ID.

**Marker ID**:
The identifier under which the game registers a scene marker. The scene markers that hold the same marker ID are its holders.

**Duplicate marker ID**:
A marker ID that more than one scene marker holds.

**Replacement ID**:
A new, unique marker ID that repairs a duplicate marker ID by replacing the ID of one of its holders.

**Starter placement marker**:
A scene object that a new-game car placement reads for its track location. Its object position is not used.

**New-game car placement**:
One placement of a new-game setup that places a cut of starting cars at its starter placement marker's track location.

**Car cut**:
The cars that one new-game car placement places together. Its front stands at the track point, and the rest extends along the track in the direction that the track location names.

**Placement rule**:
The game's own rule for whether a setup's car cuts fit on the track at their track locations.

**Track location**:
The place on a track that a track binding names, as a segment, an end and a distance along it. A starter placement marker's track location is the one its new-game car placement reads. It is distinct from the marker's object position.

**Track point**:
The map position on the loaded track where a track location lies. A track location that does not resolve on the loaded track has no track point. For a starter placement marker, it is where the front of the car cut stands, not the marker's object position.

**Named location**:
The track location under which the game registers an enabled, active scene marker by its marker ID. It is distinct from the marker's stored track location, and a marker whose location does not resolve has none.
_Avoid_: Listed track location

**Binding slot**:
The place on a scene marker, a crossing pad included, where the marker's track binding is held and edited on the Connections tab. Crossing coverage and construction segments are not binding slots.

**Wreck setting**:
The setting of a new-game car placement that decides whether its cars start derailed as a wreck.

**Grade-crossing setup**:
An existing crossing's controller with the scene objects below its root other than its whistle signs, such as crossing masts, the bell, detection spans and crossing pads.

**Crossing mast**:
One post of an existing crossing that carries its crossing light and, on a child at the same place, its gate. It is one inventory entry.

**Unprotected crossing**:
A road crossing without a crossing controller, whose root holds one or more crossing pads with whistle signs and crossbucks. It is distinct from an existing crossing.

**Crossbuck**:
The X-shaped road sign beside an unprotected crossing.

**Bell definition**:
A definition in the game's content library of the kind Bell, which holds a bell's audio and is named by its identifier. The bell's supported source pattern takes a bell definition as its equipment source.

### Editor terminology

**Scene patch editing**:
The work of making and changing scene patches in the patcher, as distinct from application at load. User-facing text uses plain words such as "edit", "editing" or "<thing> change" instead of this term.
_Avoid_: Authoring, patch authoring

**Outliner**:
The searchable tree of inventory scene objects beside the inspector in scene patch editing. It has two views, By category and By place.
_Avoid_: Object list

**Outliner view**:
One of the two arrangements of the outliner: By category, which lists category, kind, place and then the objects, or By place, which lists place, category, kind and then the objects. Both views hold the same rows.
_Avoid_: Grouping, mode

**Inspection only**:
The state of an inventory scene object that no supported family covers and that the native map editor does not edit. The outliner lists it under its category, marked inspection only, and the Object tab gives the reason it is not yet supported.
_Avoid_: Inspection only group, Inspection only category

**Problems filter**:
The outliner filter that limits the list to scene objects with a detected problem.

**Problem**:
A defect of one scene object that the modder can repair with the patcher for that object, such as a duplicate marker ID or a broken binding.

**Recheck**:
The explicit action that compares an inspected scene object with the scene patch file again, applies the file's state where they differ, and lifts the block of an incomplete cleanup once they agree. A refresh of the inventory alone applies nothing.

**Inspector**:
The region of scene patch editing that shows the inspected scene object's values, dependencies and actions.
_Avoid_: Properties panel

**Object heading**:
The inspector's title for the inspected scene object.

**Command band**:
The commands and session status at the top of scene patch editing, split at the column divider into status lamps over the outliner and object commands over the inspector.
_Avoid_: Toolbar

**Status lamp**:
A colored dot with a state word that shows session status as information, not as a command.

**Guided mode**:
An inspector mode that completes one supported operation for a pinned target through the Target, Values, Review and Apply steps.
_Avoid_: Wizard

**Guided task**:
One supported operation carried out in Guided mode, with its own pinned target and draft.

**Helper marker**:
A world marking that shows an invisible scene object at its physical placement, so that the modder can place it. A helper marker is the square marker of an invisible scene object.

**Square marker**:
The marker, a square with a plus, of a scene object in the world, or of a game-defined group beyond 125 metres. A click on a scene object's square marker makes that object the candidate; a click that picks a group's square marker opens the group's choice list.

**Object frame**:
The outline around a scene object in the world, or around a game-defined group beyond 125 metres, which shows within the frame distance.

**Frame distance**:
The camera distance beyond which a scene object or a game-defined group shows only its square marker.

**Candidate**:
The scene object that a click or a choice from the choice list proposes, framed in blue, before Confirm makes it the inspected object. A candidate in a game-defined group beyond 125 metres shows its own square marker and object frame. No operation uses a candidate.

**Choice list**:
The native dropdown next to the pointer that lists the scene objects of one click: those of square markers that overlap at the pointer, with a game-defined group's objects in place of its square marker, or the objects of a game-defined group that the click picks. A choice from it makes the candidate.
_Avoid_: Picker, popup

**Game-defined group**:
Scene objects that are one thing in the game and that the world shows as one square marker and one object frame beyond 125 metres: a grade-crossing setup, a crossing without a controller, a station, a rail bridge construction or an industry storage display. A game component holds most of them together, so their objects can stand apart in the scene hierarchy. A crossing without a controller has no such component: its crossing object holds its crossbucks and pads. Each object of a game-defined group keeps its own inventory entry and edits. Compare baked scenery group, which is placed as a whole.
_Avoid_: Cluster, marker group
