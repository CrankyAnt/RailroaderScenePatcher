<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Railroader Scene Patcher

This context covers scene changes supplied by map mods and authored for the Railroader map editor.

## Language

**MapMod**:
A mod distributed as an asset pack that supplies a Railroader map definition or changes to a map. The term includes modifications to an existing map and packs that provide a new map; it does not mean that several packs are edited together.

**Map definition**:
The game's identified definition of a selectable map, with its map data, referenced map layers and terrain sources. A definition and its containing asset pack are distinct.

**Native map layer**:
A Map definition referenced by another Map definition as inherited map content. An inherited layer is source context, not another active edit target.

**Source scene**:
A Unity scene that supplies existing scene objects to a loaded map. Sharing a source scene and inheriting native map data are distinct relationships.

**Scene patch**:
MapMod-supplied desired changes to supported scene objects and their relationships.
_Avoid_: Patch history, movement script

**Patch document**:
The document that expresses a MapMod's scene patch.
_Avoid_: Patch history

**Scene object**:
An object in a loaded map scene that may have physical placement, activity, logical relationships, or dependent state.

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

**Supported operation**:
A scene change that the patcher can author and apply for an applicable object and relationship.

**Coherent operation**:
A single user-recognizable scene change with one outcome and one restoration boundary.

**Registration**:
An object's membership in a game-managed graph or other structure that makes it available to related game behavior.

**Authored addition**:
A scene object added through a MapMod's scene patch rather than an existing object in the loaded map. Native objects that are new to a particular MapMod are not automatically authored additions.

**Existing crossing**:
A protected road crossing already present in the map, whose track coverage or equipment can be extended or corrected.
_Avoid_: New standalone crossing

**Map context**:
The loaded map and resolved map layers against which scene-patch targets are identified.

**Supplying MapMod**:
The MapMod that owns a scene-patch document, regardless of which source map or pack contains a target object.
_Avoid_: Target owner

**Active edited MapMod**:
The supplying MapMod that owns the selected Map definition being authored in the editor. Other packs can supply inherited content without becoming edit targets.

**Effective underlying state**:
The state that remains for an existing object without a particular modification by its supplying MapMod, including the applicable native context and other retained modifications in that editing or runtime context.

**Native pack load order**:
The order in which Railroader loads enabled asset packs. It is distinct from the order in which native map layers compose a map.

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
