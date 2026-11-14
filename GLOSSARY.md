<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Railroader Scene Patcher

This context covers scene changes supplied by map mods and made in the Railroader map editor.

## Language

### Railroader terminology

These terms retain the meanings in Railroader's [asset-pack guide](https://docs.railroadergame.com/modding/asset-packs/), [map-modding guide](https://docs.railroadergame.com/modding/map-modding/) and [load-order reference](https://docs.railroadergame.com/modding/load-order/).

**Asset pack**:
The package in which a Railroader mod distributes its content, including definitions, data, scripts or bundled assets. A pack and a Map definition within it are distinct.

**Map definition**:
The game's identified definition of a selectable map, with its map data, referenced map layers and terrain sources. A definition and its containing asset pack are distinct.

**Map layer**:
A Map definition referenced by another Map definition as inherited map content. An inherited layer is source context, not another active edit target.
_Avoid_: Native map layer

**Load order**:
The order in which Railroader loads enabled asset packs. It is distinct from the order in which map layers compose a map.
_Avoid_: Native pack load order

### Unity terminology

Unity scene retains its meaning in Unity's [scene documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/CreatingScenes.html).

**Unity scene**:
A Unity container for scene objects, including their hierarchy and components. A Unity scene is distinct from Railroader's Map definition and map layers.
_Avoid_: Source scene

### Patcher terminology

These are project terms for editing and applying scene patches, not additional Railroader definition types or Unity object types.

**MapMod**:
An asset pack that supplies a Railroader Map definition or changes to a map. This includes modifications to an existing map and packs that provide a new map.

**Scene object**:
An object in a Unity scene loaded for the map that may have physical placement, activity, logical relationships, or dependent state.

**Independent board**:
A board scene object whose physical placement is independent of any logical track binding.

**Terrain output**:
Content generated for terrain tiles from the current scene state, including vegetation affected by an independent board.

**Scene patch**:
MapMod-supplied desired changes to supported scene objects and their relationships.
_Avoid_: Patch history, movement script

**Patch document**:
The document that expresses a MapMod's scene patch.
_Avoid_: Patch history

**MapMod value**:
A value that a supplying MapMod's patch document sets for a scene object. Without it, the object keeps its effective underlying state.
_Avoid_: Authored value, authored state, authored binding, authored activity

**Physical placement**:
An object's position and rotation in the map.
_Avoid_: Binding

**Logical binding**:
A relationship from a scene object to another object or track whose meaning is defined by the game.
_Avoid_: Placement

**Track-following placement**:
Physical placement defined relative to a selected track or one of its endpoints.

**Existing target**:
A supported scene object that already exists in the loaded map context and is identified for a scene patch.

**Modded object**:
An existing scene object that a MapMod's scene patch changes.
_Avoid_: Authored object

**Supported operation**:
A scene change that the patcher can edit and apply for an applicable object and relationship.

**Coherent operation**:
A single user-recognizable scene change with one outcome and one restoration boundary.

**Registration**:
An object's membership in a game-managed graph or other structure that makes it available to related game behavior.

**Added object**:
A scene object that a MapMod's scene patch adds, rather than an existing object in the loaded map. Native objects that are new to a particular MapMod are not automatically added objects.
_Avoid_: Authored addition

**Existing crossing**:
A protected road crossing already present in the map, whose track coverage or equipment can be extended or corrected.
_Avoid_: New standalone crossing

**Map context**:
The loaded map and resolved map layers against which scene-patch targets are identified.

**Supplying MapMod**:
The MapMod that owns a scene-patch document, regardless of which source map or pack contains a target object.
_Avoid_: Target owner

**Active edited MapMod**:
The supplying MapMod that owns the selected Map definition being edited in the editor. Other packs can supply inherited content without becoming edit targets.

**Effective underlying state**:
The state that remains for an existing object without a particular modification by its supplying MapMod, including the applicable native context and other retained modifications in that editing or runtime context.

**Informational overlap notice**:
A notice that applicable patch documents from different supplying MapMods attempt to modify the same resolved existing scene object. It indicates possible incompatibility, not a proven conflict.

**Supported source pattern**:
A supported equipment behavior with its required components, relationships and available dependent data. A model name or visual resemblance alone does not identify a pattern.

**Eligible equipment source**:
An available equipment source that conforms to a supported source pattern and satisfies its applicable requirements.

**Format version**:
The version that identifies the meaning of patch-document data. It is separate from the release versions of the patcher and supplying MapMod.

**Checked build**:
A Railroader build for which the patcher's relevant game functions and prerequisites have been explicitly checked.

**Track group**:
A named set of track that the map enables or makes available as a whole, for example through map features.

**Group-driven activity**:
An object's activity as the game sets it from whether its track group is enabled.
_Avoid_: Group-following object

**Group subscription**:
An object's dependence on its track group, through which enabling or disabling the group changes the object's activity.

**Explicit disable**:
A MapMod value that holds an object inactive, which takes precedence over later track-group activation.

**Circuit**:
One detection unit of an existing crossing's controller, whose approach and island spans decide when the crossing's equipment activates.

**Crossing coverage**:
The tracks each circuit of an existing crossing detects, set per circuit by a MapMod as omitted, explicitly selected tracks or explicitly detached. Proximity never assigns it.

**Owned span**:
A detection span that a MapMod's coherent operation constructs and registers for selected tracks. Removal and cleanup remove owned spans; native spans are never removed.

**Detection addition**:
A circuit that a MapMod adds at an existing crossing: explicitly selected tracks and areas for its spans, with an identity scoped to the supplying MapMod. It adds detection without changing the crossing's native circuits. It is not an Added object.

### Editor terminology

**Scene patch editing**:
The work of making and changing scene patches in the map editor, as distinct from application at load. User-facing text uses plain words such as "edit", "editing" or "<thing> change" instead of this term.
_Avoid_: Authoring, patch authoring

**Outliner**:
The searchable list of inventory scene objects beside the inspector in the production editor.
_Avoid_: Object list, tree

**Problems filter**:
The outliner filter that limits the list to scene objects with a detected problem.

**Inspector**:
The editor region that shows the inspected scene object's values, dependencies and actions.
_Avoid_: Properties panel

**Object heading**:
The inspector's title for the inspected scene object.

**Command band**:
The row of object-level commands and status in the inspector, below the object heading.
_Avoid_: Toolbar

**Guided mode**:
An inspector mode that completes one supported operation for a pinned target through the Target, Values, Review and Apply steps.
_Avoid_: Wizard

**Guided task**:
One supported operation carried out in Guided mode, with its own pinned target and draft.
