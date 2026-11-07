# Ownership probe protocol

This protocol records the proposed procedure. The [research report](../OwnershipReadiness.md) records which steps were observed and their limitations. Use only with the runtime lease and a disposable save. Preserve existing mods, saves and preferences.

## Build and prerequisites

The probe targets .NET Framework 4.7.2 and references `Railroader.Modding.dll` and `UnityEngine.CoreModule.dll` in the installed game's `Railroader_Data/Managed` directory. From the repository root, substitute the local game path and a temporary build directory:

```powershell
dotnet build Research/OwnershipProbe/Probe.csproj -p:RailroaderManaged='<game>/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='<temp>/obj/' -p:OutputPath='<temp>/out/'
```

The build on 2026-11-07 succeeded with zero warnings and errors against `Railroader.dll` SHA-256 `8e9acfd583c1da79655c970773d42f2179e999f62dc36a3c6bc3a7b94c27a91e`. The probe does not modify map data. It logs pack identifiers, writable flags, classified roots, callback events and component counts. Counts include loaded objects returned by `Resources.FindObjectsOfTypeAll`, so they do not prove those objects are active, registered or safe to mutate. Periodic samples occur approximately every ten seconds and cannot establish exact callback order between samples.

## Stage

1. Acquire `C:\TMP\RailroaderMapResearch\RuntimeLease` using `New-Item -ItemType Directory` without `-Force`. If it exists, wait for its owner. Check that Railroader is not running and verify the game assembly hash. Record the active game version, configuration, mod selections and preference state. Back up any configuration that the test changes outside the game directories, and record its hash.
2. Confirm these exact destination folders do not already exist under the game's `ModDev` directory. Stop if any exists. Copy `Fixtures/Probe`, `Fixtures/MapA` and `Fixtures/MapB` respectively to `OwnershipResearchProbe`, `OwnershipResearchA` and `OwnershipResearchB`. Copy the built `OwnershipProbe.dll` into `OwnershipResearchProbe`. Do not overwrite an existing mod.
3. Enable the probe and both fixture packs as the game requires. The two maps depend on the probe; map B also depends on map A and layers on A. If unsafe DLL loading is disabled, record its original setting before changing it and restart as required by the [DLL guide](https://docs.railroadergame.com/modding/dll-mods/). Record any refusal rather than assuming a dependency loaded.

## Observe

For each step, record the visible editor selection, timestamps, success or refusal and the relevant `[OwnershipProbe]` log lines. Wait at least ten seconds after load and after returning to the menu, so a periodic snapshot can occur. `sceneLoaded`, `sceneUnloaded`, `OnWorldLoad`, `OnWorldUnload`, `OnModLoad` and `OnModUnload` are event markers. Snapshot lines show `Identifier`, `IsEditTarget`, map data and layer root classifications, definition owner root, ordered pack IDs and store writability. `fixture-A`, `fixture-B`, `probe` and `game-core` identify classified paths. `other-redacted`, `null`, `unavailable`, missing lines and `ERROR` are unknowns, not successful matches.

1. Open map A in the definition editor, choose Edit Map, observe, then exit without saving. Record the selected target and loaded packs.
2. Repeat with map B. Check whether B is the edit target and definition owner while A is a loaded cross-pack layer. Compare the map-data root, store root and writable flag. Do not infer a patch destination if the evidence conflicts or is unavailable.
3. Create a new disposable ordinary save using map B, load it, observe, return to the menu, and re-enter the same disposable save. Capture callback order and component counts for each entry and unload. A loaded pack is not by itself evidence that arbitrary JSON was discovered or that the pack is relevant to a particular map.
4. Review the probe output for reflection exceptions, incomplete snapshots and unsupported type assumptions. If a fixture does not load or the probe fails, preserve the refusal or error as a finding. Do not treat a missing sample as zero objects.

The probe logs pack identifiers and may log exception details. Inspect captured output for private paths and other sensitive information before retaining or publishing it.

## Restore

Exit Railroader before restoring. Remove only the three exact fixture directories created in this run and the explicitly identified disposable save. Restore changed preferences and mod selection from the recorded backups, verify their hashes and confirm the original selections. Verify the fixture directories are absent and the normal saves and existing mods remain. Release only the lease created by this run. Keep the authored source and sanitized observations; do not retain or publish game assemblies or assets.
