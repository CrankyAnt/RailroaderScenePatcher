<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Read the terrain height in a native journey

A Railroader journey that moves a scene ground mask, such as the flattening mask of `Wilmot Station`, compares the terrain height before and after the edit. The test mod `Tests/TerrainHeightReadout` reads that height as a number, so the comparison doesn't rest on a screenshot alone. It reads the built terrain and changes nothing in the world.

## Build and install the readout

Build the mod beside the patcher build of the journey:

```powershell
dotnet build Tests/TerrainHeightReadout --configuration Release -p:GameManaged=<Railroader_Data/Managed> -p:BuildArtifactsRoot=<build root>
```

Install `TerrainHeightReadout.dll` and its `Info.json` from `<build root>\TerrainHeightReadout\bin\Release\net472` in a mod folder of their own in the test home, and enable the mod in the test profile. It depends on no other mod.

## Read the heights

1. Start Railroader and load the world. The mod shows a window titled **Terrain height readout (test only, F10 hides)**. Press F10 to hide or show it.
2. **Pointer** shows the map x and z of the built terrain under the mouse pointer and the terrain height there.
3. For each of the places **A** and **B**, such as the old and the new position of a moved mask, type the map x and z, or point at the terrain and click **Pointer** on the row of that place. The line below the row shows the height at that place.
4. Click **Log heights** to write one line per place to the Player log, for example `[Terrain height readout] A x=16525.90 z=2300.80 height=123.456`.

Read the heights at the same places before the edit, after it, after **Undo**, and after **Remove modification**. Wait until the **Changes** tab shows `Terrain updated` before you read, because the game builds the terrain after the edit returns.

## What the readout reports

Positions and heights are map coordinates, the coordinates the patcher shows and writes. The mod converts them with `Helpers.WorldTransformer`, so they stay the same when the floating origin moves. The height is the built terrain tile's height at that place (`Terrain.SampleHeight` plus the tile's own height), with three decimals. Where no terrain tile is built, the line reads `no built terrain tile`. The pointer reading uses the terrain under the pointer only; buildings and other colliders in front of it are passed by.
