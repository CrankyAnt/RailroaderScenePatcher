# Changelog

Notable changes to this project are recorded here, newest first.

## 0.5.0

- In multiplayer, the host and every client apply the scene patches from their own installed MapMods to their own scene when their world loads, and each checks the map, target and capability locally. A failure on one player's game is reported there only. The patcher adds no synchronization or admission check; Railroader's own pack and version checks still decide who can join. Live host and client sessions with this release have not been tested yet.

- Apply the scene patches of several loaded MapMods in Railroader's load order. A MapMod applies only when it declares the patcher dependency and its source map matches the loaded map or one of its layers. When MapMods change the same existing object, an informational game message names the object and the MapMods; it reports a possible incompatibility, chooses no winner and does not stop application. A failure in one MapMod's document leaves the other MapMods' work in place.

- Remove a marker repair even when its original track is missing. Restore the captured native reference and warning; Undo and Redo recover the repaired and broken states without changing unrelated warning rows.

- Make an object follow an explicitly selected track segment location or endpoint, in the editor and through `placement.mode = "trackFollowing"` in `scene-patches.json`. The object keeps its position and orientation relative to that track; it moves with the track in the editor and is placed from the track's current pose on every load. Documents with a track-following placement require patcher 0.5.0.
- Generic track markers whose activity follows a track group can be edited. Rebinding moves the group subscription with the marker. An explicit disable holds the object inactive when the group is enabled later; without it the group keeps switching the object. Clearing the authored activity or removing the modification returns the object to its group. Documents with an `active` value on a track-marker object require patcher 0.5.0.
- Runtime failures produce structured game warnings with the supplying MapMod, operation, target when resolved, error, and patcher and game versions. For eligible dependency-free relationship-only marker repairs, the patcher tries repair before the game's native marker check. If that route is unavailable, it removes only the exact current warning row after a successful operation and separate verification of the binding and dependent state. Reporting adds no document, schema or capability requirement.

- Remove the duplicated source prefix from runtime diagnostic text. The originating source remains available as the native warning originator.

- Add detection for explicitly selected extra tracks at an existing grade crossing, in the editor and through an `addition` entry in `relationships.crossingCoverage`. An addition has an ID that is unique in its MapMod's document, and the tracks and area of its three spans. The patcher builds one circuit with its own spans for it on every load, reuses it on a repeated load and removes it when the entry is deleted. Native circuits keep their coverage.
- Correct the detection coverage of existing grade crossings, in the editor and through `relationships.crossingCoverage` in `scene-patches.json` (`crossing-coverage-v1`). Each circuit, named by its native island span, keeps its coverage when omitted, covers explicitly selected tracks, or is detached. Native spans stay registered and unchanged, so a still-valid old route stops triggering the crossing; other circuits keep working. The editor previews the effect on every circuit before applying. Documents with crossing coverage or a crossing-coverage target require patcher 0.5.0.
- Set or replace the track location of crossing pads in the editor and through `relationships.trackMarker` in `scene-patches.json` (`crossing-pad-v1`). The pad's graph registration follows the binding, and the game's pad generation rebuilds the pad geometry and culling at the new location; Undo, Redo, removal and failed edits generate the pad again from the restored location. Pads cannot be detached; disable them to hide them. Documents with a crossing-pad modification require patcher 0.5.0.

## 0.4.0

- Set, replace or detach the track location of Generic track markers, such as yard-limit and station signs, in the editor and through `relationships.trackMarker` in `scene-patches.json` (`track-marker-v1`). The marker's track-graph registration follows the binding; the object does not move. Placement and activity of a marker object are separate settings. Mileposts, crossing pads, crossings and markers whose activity follows a track group are inspection only. Documents with a relationship or a track-marker target require patcher 0.4.0.

## 0.3.0

- Move, rotate, disable and restore the Ela bridge with its colliders and culling, in the editor and through `scene-patches.json` (`bridge-colliders-v1`). Separate track-owned trestles and construction scenery stay unchanged. Documents with a bridge modification require patcher 0.3.0.
- Move, rotate, disable and restore independent boards with their culling and terrain mask, in the editor and through `scene-patches.json` (`independent-board-v1`). The editor tracks the terrain output the game regenerates, reports writes that do not complete and can request them again. Documents that move a board other than the Sylva location sign require patcher 0.3.0.
- A capability that is incompatible with the running game build fails only the modifications that name its structure.
