<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Railroader Scene Patcher

This context covers scene changes supplied by map mods and authored for the Railroader map editor.

## Language

**MapMod**:
A mod that supplies a Railroader map definition or changes to a map.

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
The supplying MapMod currently being authored in the map editor.

**Effective underlying state**:
The state of an existing object after native and inherited map changes take effect, before this MapMod's scene patch is applied.

**Format version**:
The version that identifies the meaning of patch-document data. It is separate from the release versions of the patcher and supplying MapMod.

**Checked build**:
A Railroader build for which the patcher's relevant game functions and prerequisites have been explicitly checked.
