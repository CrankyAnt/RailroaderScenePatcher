<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Terrain target preflight, Boot03

The observed-tree button rejected the request before moving the board. Its guard expected the circle mask radius to be 2.5 m, but the baseline recorded a mask radius of 1.5 m and a falloff of 1 m. The separate culler radius was 2.5 m. The guard has since been corrected in the working source; that correction was not part of Boot03.

The baseline recorded one generated tree inside the target's 1.5 m core. Its generated, handed-off and loaded arrays were the same object, the tree manager was present, and terrain work was idle with no pending invalidation. The mask had a registered `CutTrees` modifier with value 1. The failure occurred before the added combined preflight and before mutation, so this run does not establish whether moving the mask changes generated trees.

The archived probe log is `C:/TMP/RailroaderMapResearch/Observations/SceneOperationBatch/Boot03/TerrainOutputProbe-TerrainOutputProbe.log`, SHA-256 `40402E3AEB2A22BFDA62B9CD6FB1F011A1C65E05B3FF03B6F04E39BD048832A9`. The frozen Boot03 source `TerrainOutputProbe.cs` hashes to `D55ECB2F53A1B2032DE3C592B273421896CE13E4BD9C36A02F68732E8063F407`; the frozen and installed DLLs both hash to `7C8E2B64C7D9EA74A73E1374E43CD4E09B652AEB7E6D4E4C1540BF250DC75A87`. The frozen files are under `C:/TMP/RailroaderMapResearch/Observations/SceneOperationBatch/Boot03/`. These build identities do not identify every vendor scene asset.
