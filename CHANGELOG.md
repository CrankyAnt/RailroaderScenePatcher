# Changelog

Notable changes to this project are recorded here, newest first.

## 0.3.0

- Move, rotate, disable and restore the Ela bridge with its colliders and culling, in the editor and through `scene-patches.json` (`bridge-colliders-v1`). Separate track-owned trestles and construction scenery stay unchanged. Documents with a bridge modification require patcher 0.3.0.
- Move, rotate, disable and restore independent boards with their culling and terrain mask, in the editor and through `scene-patches.json` (`independent-board-v1`). The editor tracks the terrain output the game regenerates, reports writes that do not complete and can request them again. Documents that move a board other than the Sylva location sign require patcher 0.3.0.
- A capability that is incompatible with the running game build fails only the modifications that name its structure.
