<!--
SPDX-FileCopyrightText: 2026 CrankyAnt
SPDX-License-Identifier: MIT
-->

# Applicable-object group and span probe

This variant of the [applicable-object probe](../ApplicableProbe/README.md) adds a temporary `sy-db` group-activation test and a `Pipr` crossing-island endpoint test. It is independently authored research tooling for a disposable fixture, not a production integration. The group test changes native group state for every object affected by that group. Run without trains and never save the session. The span test changes only the lower endpoint by 0.25 m on the existing segment, repeats it and restores it immediately. It does not test a broken endpoint or train, gate, light or bell behavior.

Build from the repository root:

```powershell
dotnet build Research/ApplicableProbeNext/ApplicableProbe.csproj -p:GameManaged='D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='C:/TMP/ApplicableProbe18GroupSpan/obj/' -o C:/TMP/ApplicableProbe18GroupSpan/out
```

Install only the reviewed build and `Info.json` in a separate disposable profile while the game is stopped. Load the research fixture. Restore other captured objects before running either test. Capture the probe log and check the group cleanup and span restoration lines. If any check fails, abandon the disposable session without saving. The [research report](../ApplicableSceneOperations.md) names the executed DLL hash, selected observations and remaining unknowns. `Snapshots/` retains the corresponding independently authored source; file names are DLL hashes, not source hashes.
