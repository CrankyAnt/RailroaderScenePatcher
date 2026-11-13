# Changelog

Notable changes to this project are recorded here, newest first.

## 0.5.0

- Make an object follow an explicitly selected track segment location or endpoint, in the editor and through `placement.mode = "trackFollowing"` in `scene-patches.json`. The object keeps its position and orientation relative to that track; it moves with the track in the editor and is placed from the track's current pose on every load. Documents with a track-following placement require patcher 0.5.0.
- Generic track markers whose activity follows a track group can be edited. Rebinding moves the group subscription with the marker. An explicit disable holds the object inactive when the group is enabled later; without it the group keeps switching the object. Clearing the authored activity or removing the modification returns the object to its group. Documents with an `active` value on a track-marker object require patcher 0.5.0.

## 0.4.0

- Set, replace or detach the track location of Generic track markers, such as yard-limit and station signs, in the editor and through `relationships.trackMarker` in `scene-patches.json` (`track-marker-v1`). The marker's track-graph registration follows the binding; the object does not move. Placement and activity of a marker object are separate settings. Mileposts, crossing pads, crossings and markers whose activity follows a track group are inspection only. Documents with a relationship or a track-marker target require patcher 0.4.0.

## 0.3.0

- Move, rotate, disable and restore the Ela bridge with its colliders and culling, in the editor and through `scene-patches.json` (`bridge-colliders-v1`). Separate track-owned trestles and construction scenery stay unchanged. Documents with a bridge modification require patcher 0.3.0.
- Move, rotate, disable and restore independent boards with their culling and terrain mask, in the editor and through `scene-patches.json` (`independent-board-v1`). The editor tracks the terrain output the game regenerates, reports writes that do not complete and can request them again. Documents that move a board other than the Sylva location sign require patcher 0.3.0.
- A capability that is incompatible with the running game build fails only the modifications that name its structure.
