<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Terrain output trial, Boot04

On the isolated `crankyant/scene-operations-map` fixture, the Sylva Int board and its registered `CutTrees` mask were moved to a previously observed generated tree, then restored. After the relevant native tile rebuild and idle terrain state, the target area's generated tree count within 4.5 m fell from one to zero and the tile total fell from 1,089 to 1,088. After restoration and another relevant rebuild, both returned to baseline. The generated list was handed to and loaded by the GPUI terrain consumer in each delayed sample. The [selected telemetry](Telemetry.md) records the fixed sites, counts, digests and tile bounds.

All three sampled 81-point height grids matched their respective baselines after the move and restoration. This observation covers one generated tree and one mask placement. It does not establish behavior for all vegetation, other placements or persistence. The camera showed the original board area; the target tree, roughly 93 m away, was not individually distinguishable. The array and consumer observations therefore do not establish a visible rendered change at the target.

The [frozen build manifest](../BuildIdentity.json) identifies the authored DLL as SHA-256 `4F56B243C078EBF4CE7EBC2FD93181B5373B208A510E51BBA7B88C075B69F2E5`, matching the frozen DLL, and records the [frozen authored inputs](../AuthoredSources/Geometry/TerrainOutputProbe.cs.txt) used for the trial. The manifest is build evidence; the separate archived runtime log at `C:/TMP/RailroaderMapResearch/Observations/SceneOperationBatch/Boot04/TerrainOutputProbe-TerrainOutputProbe.log` hashes to `ED6C686CAC45A10BAF9E3DCA0F982C23221ECE82567B50C0B0B2210FA64F45AB`. Its final unload recorded `dirty=False`. These identities do not cover every vendor scene asset.
