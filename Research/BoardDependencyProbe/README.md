<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Board dependency probe

Prepared for source review. Do not install or run before review. Use only the disposable `crankyant/scene-operations-map` world and never save. Focus the board before capture. Capture requires one active `World/Signs/Loc: Sylva Int`, its expected three-node hierarchy, the culler and circle mask, and no track marker. Move applies an absolute 5 m world-X move and 15-degree world-Y rotation from the capture. Repeat uses the same captured pose. Snapshot can run after waiting several frames. Restore writes and verifies the captured local pose. Recapture is forbidden while dirty. The probe checks for origin and parent changes before move and snapshot. Restore attempts the original local pose even when origin changed and reports that condition. Restore before leaving; if anything fails, abandon without saving. Check both probe and game logs.

Snapshots record node poses and activity, component and renderer state, mask fields and registered modifier keys, and the culler token's current sphere. They do not establish visibility, collision, vegetation or actual terrain masking. The probe does not change the mask or request a culler update. Do not shift origin during a captured test; this probe does not test origin-shift behavior.

Metadata inspection of the installed `Map.Runtime.dll` found that `CircleMapMask.MakeMaskDescriptor` reads the current transform position and that `MapMaskBase.ApplyModifiers` creates modifiers from that descriptor. `MaskComponentBase.OnEnable`, `OnValidate` and `Rebuild` call `ApplyModifiers`; `OnDisable` removes modifiers. This identifies rebuild paths, but does not establish whether another caller or the game updates a modifier after a transform move. Modifier keys alone do not prove terrain output.

Build from the repository root with game assemblies and an external intermediate and output directory:

```powershell
dotnet build Research/BoardDependencyProbe/BoardDependencyProbe.csproj -p:GameManaged='<game>/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='<temporary-directory>/obj/' -p:OutputPath='<temporary-directory>/out/'
```

The prepared staged DLL SHA-256 is `558267DFCB068FF146E7308CD8FF38858D8C11F7F322FB2E91FFC3AA9606F318`; its source SHA-256 is `41260AFA0BD96CB91CB03792B7660B3742643862605A6EF643E6218D6F4FB56F`. Frozen source is in `Snapshots/` under the DLL hash. The earlier one-frame snapshot `6A5DEFAEAFFA1CC0DBE83BFA37233484E88503F44BB11DEB2E699BA7D0053D2D` was not run. The installed `Railroader.dll` SHA-256 was `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`; `Map.Runtime.dll` was `9C52BD1FA8AA6D3D4417484F2002F985F6F4E01CBCD3C96E7675C4F19B8F0767`. Mono.Cecil identified the private fields and methods. The probe fails if the field shape differs.
