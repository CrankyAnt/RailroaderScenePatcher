<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Terrain output trial, Boot02

On the isolated `crankyant/scene-operations-map` fixture, the Sylva Int board was moved 5 m, rotated 15 degrees, moved to the same pose again, disabled and restored. The sampled `CutTrees` modifier was registered at baseline and after restoration. A relevant terrain tile rebuild followed each move, disable and restore. The delayed samples reported no pending invalidation and idle terrain work. Restoration reported `originChanged=False`.

The generated tree array on the loaded tile contained 1,089 instances at baseline. The generated, handed-off and loaded arrays were the same object, and the GPUI tree manager was present. Both fixed observation sites had zero trees within the 2.5 m mask extent and the 4.5 m sample radius. The nearest generated tree to the original site was 93.31607 m away, at game position `25772.99,632.9081,393.795837`. The 81-point sampled height hashes and the empty nearby tree hashes matched baseline in the delayed snapshots. These measurements do not show whether `CutTrees` removes generated trees: there were no generated trees in either sampled mask area. They do not establish rendered pixels, persistence or autosave behavior.

The frozen runtime log is retained locally at `C:/TMP/RailroaderMapResearch/Observations/SceneOperationBatch/Boot02/TerrainOutputProbe-TerrainOutputProbe.log`, SHA-256 `935A0AA07B471E74704FF1D010C2FB393413B54F26C51AA56CC070C72978799A`. The separate frozen build manifest identifies the authored probe DLL; hashing the frozen DLL yielded `17EAA5B309C234D35112904FEE7EF7931A17679C29C9A2317AFA5CC327A06CB1`. Its frozen source hashes are listed below. These identify the code used for this observation, not the subsequently changed source in this repository. The build manifest is build evidence, and the runtime log is separate observation evidence. The hashes do not identify every vendor scene asset.

| Frozen authored source | SHA-256 |
| --- | --- |
| `TerrainOutputProbe.cs` | `3C7C46A549BB68C5F0E6CE39DE1EB7624C9AF8714AB9379D870B0CD33A269093` |
| `TerrainSamples.cs` | `587CF95B8835CFFECD9B65B154E994CC6478694E12AE55E37EF8A2B5C062676E` |
| `TerrainTreeOutput.cs` | `A2873A376583218DE0F3DCFF85EC8CAFD66F40232D9471DAA910822CDEBBA57C` |
| `TerrainOutputProbe.csproj` | `007698C0A327FCAD789F933C8492EEAB47B52C4A82A4A875D4F068D7624DCAF4` |
