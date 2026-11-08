<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Selected terrain telemetry, Boot04

The three fixed game-coordinate sites were the board baseline `25692.9766,633.4856,441.8128`, its five-metre offset `25697.9766,633.4856,441.8128`, and the tree target `25772.99,633.4856,393.795837`. The target tree's recorded position was `25772.99,632.9081,393.795837`. Mask radius was 1.5 m with 1 m falloff; tree sample radius was 4.5 m. Distance checks use the horizontal plane.

| Phase | Tile tree total | Target core / extent / sample count | Target sample digest | Pending / idle |
| --- | ---: | --- | --- | --- |
| Baseline | 1089 | 1 / 1 / 1 | `01A913A2027222168CD3EEA1D751ED5113F5764A2328E10928D089AD78895C52` | 0 / true |
| Preflight | 1089 | 1 / 1 / 1 | `01A913A2027222168CD3EEA1D751ED5113F5764A2328E10928D089AD78895C52` | 0 / true |
| Delayed after move | 1088 | 0 / 0 / 0 | `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855` | 0 / true |
| Delayed after restore | 1089 | 1 / 1 / 1 | `01A913A2027222168CD3EEA1D751ED5113F5764A2328E10928D089AD78895C52` | 0 / true |

At all four phases the target tile's generated, handed-off and loaded tree arrays were the same object, `IsTreeInstancesLoaded=True` and the manager was present. The original and five-metre-offset samples had zero generated trees within 4.5 m in all phases. Each site's 81-point height sample matched its own baseline after move and restore. The height digest combines height rows with any `TerrainData.treeInstances` rows; those tree counts were zero in these samples.

After the move the native event reported tile bounds centered at game coordinates `25750,0,250`, with horizontal size 500 by 500 m. The same bounds were reported after restore. These bounds contain all three sites. The origin stayed `25500,0,0`, and the immediate restore reported `originChanged=False`. The registered `CutTrees` descriptor moved to the target and returned to the original position.
