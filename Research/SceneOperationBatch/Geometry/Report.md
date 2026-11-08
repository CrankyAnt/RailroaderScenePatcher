<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Bridge contact and terrain output preparation

This report supports [bridge collision and separate scenery restoration](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/27) and [terrain output after independent board movement](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/28). Both questions remain open. The authored probes compile, but neither has been installed or executed. No collision, terrain, visual or restoration result is claimed.

## Sources and inspected identity

The starting evidence is the [operation continuation report](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuation.md) and its [assessment](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuationPlan.md). The terrain probe derives from the independently authored [board continuation probe](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/BoardContinuationProbe/BoardContinuationProbe.cs). The bridge probe uses the exact bridge and separate approach paths from the [retained bridge trial](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableSceneOperations.md#bridge-root-experiment). Earlier visible restoration does not establish physical contact or exact restoration.

Read-only Mono.Cecil inspection of the installed assemblies confirmed the following method paths. No vendor method bodies or assets are retained here.

| Assembly and method | Static result |
| --- | --- |
| `Map.Runtime.MaskComponents.MaskComponentBase.Rebuild` | Calls the virtual modifier application path. |
| `MaskComponentBase.RemoveModifiers` | Removes stored keys through `MapManager.RemoveModifier` and clears the component key set. |
| `MapMaskBase.ApplyModifiers` | Removes prior modifiers, obtains the current descriptor and registers configured targets and an optional heightmap modifier. |
| `MapManager.AddModifier` and `RemoveModifier` | Schedule bounds invalidation with a 0.5-second delay. This is a request, not a completed output update. |
| `MapManager.RaiseTileBuilt` | Raises `OnTileBuilt` with the tile data's game-coordinate bounds. |
| `MapManager` metadata | Exposes `PendingInvalidationCount` and `IsTerrainWorkIdle`. |

`Railroader.dll` SHA-256 is `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. `Map.Runtime.dll` SHA-256 is `9C52BD1FA8AA6D3D4417484F2002F985F6F4E01CBCD3C96E7675C4F19B8F0767`. The canonical local environment identifies Railroader 0.6.2 experimental build 26335745. These hashes do not identify all scene assets. Exact authored source and compiled DLL identities are in the batch [build manifest](../BuildIdentity.json).

## Bridge probe

[BridgeContactProbe.cs](BridgeContactProbe.cs) captures `World/Large Scenery/Ela/Ela Bridge` and the separate `Ela Bridge Scenery`. It records descendants, local and game-coordinate poses, activity, component identities, collider shapes and enabled state, renderer state and culling spheres. The probe captures and checks the complete collider ID set as well as each collider's shape. The separate approach is observed, never moved by the probe. A bounded operation moves the bridge five metres on world X and rotates it 15 degrees about world Y. Repeat derives from the captured baseline. Activity is a separate operation. Culling positions are explicitly refreshed after move and restore.

Restoration writes the captured root local position, rotation, scale and activity. It checks exact captured local values for all observed nodes, complete collider identities and shapes, and world poses within 0.01 metre and 0.01 degree. Snapshots report individual numeric errors. The parent must retain its captured identity and pose, and the floating origin must remain unchanged. These are proposed verification checks, not evidence that restoration succeeds in the game. Exact culling visibility, downstream subscriptions and all group dependencies are not guaranteed by these checks.

The optional contact trial creates a 0.2-metre Unity primitive sphere with a light dynamic rigidbody above an upward-facing bridge collider. A ray query finds the candidate surface; its hit is explicitly labelled as a query, not contact evidence. Only a later `OnCollisionEnter` record identifies physical contact and whether the other collider belongs to the bridge. The trial logs simulation mode and time scale without changing either. Missing contact during paused or externally driven physics is inconclusive. The ball must be removed between phases. Removal disables it immediately and requests Unity destruction. No train, passenger or gameplay collision guarantee follows from one sphere contact.

The probe refuses personal-home use, a different map, incomplete baseline capture, missing or duplicate targets, graph-independent scene replacement through destroyed targets, origin drift and changed parent identity or pose. Unload removes the owned ball and drops references without attempting to repair an unloaded world. A dirty unload is recorded. Do not save this disposable world.

## Terrain probe

[TerrainOutputProbe.cs](TerrainOutputProbe.cs) retains the prior board's component, mask and culling snapshots but removes automatic mutation and the distant origin experiment. [TerrainSamples.cs](TerrainSamples.cs) adds a native tile-build event trace, pending invalidation and idle-state readings, and output samples at both the original and five-metre-shifted positions. Both sites must have exactly one loaded terrain for baseline capture. A sample records nearby `TerrainData.treeInstances` and an 81-point height grid using counts and sorted SHA-256 digests rather than exporting terrain assets. The sampling radius is the mask radius plus falloff plus two metres. Baseline comparisons use fixed sites and require a stable origin. Restoration retains the capture so delayed post-restore output can be sampled separately.

The registered `CutTrees` descriptor from the earlier trial makes tree output relevant. This does not establish that `TerrainData.treeInstances` represents every rendered GPU vegetation path. Detail layers, GPU buffers, splat maps and rendered pixels are not sampled. Empty or unchanged tree data can therefore be inconclusive. Tile-build events include bounds so the observer can distinguish a relevant rebuild from unrelated tile work. A zero pending count or an unrelated build alone does not prove the target region was rebuilt. The script does not call a global terrain rebuild to manufacture a result.

## Pending user-present trials

Use the batch [procedure](../README.md), canonical local environment and a fresh isolated process. Each probe must restore or discard its own case before the next case begins.

For the bridge, focus and settle the origin before capture. Record visible alignment and contact at baseline. Remove the ball, apply the bounded move, wait and snapshot, then record contact separately from visible alignment. Remove the ball, repeat and compare. Toggle activity and inspect collider/culling state. Restore, wait, snapshot and repeat baseline contact. Compare the independent approach throughout. A missing contact or failed check remains an unresolved result.

For terrain, focus and settle before capture, then record the board and visible terrain at both sites. Move, wait past the scheduled invalidation, and collect delayed samples and relevant tile-build bounds. Observe visible vegetation and terrain separately. Repeat, then disable and observe. Restore and wait for the native update before the final sample and visual comparison. Do not repeat the earlier automatic-load and origin-shift trials. If no relevant update occurs, report that obstruction rather than claiming unchanged output after an update.

Both cases still require actual loader execution, telemetry, visual observations and verified restoration. Independent review of the source and clean builds are preparation only.
