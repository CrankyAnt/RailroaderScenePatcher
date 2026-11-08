<!--
SPDX-FileCopyrightText: 2026 CrankyAnt
SPDX-License-Identifier: MIT
-->

# Milepost diagnostic probe

This disposable experiment examines the existing T-46 origin and T-47 mileposts in `crankyant/scene-operations-map`. It is not a general patcher. It requires the installed game assembly used by the research report and refuses a different active map, missing or duplicate objects, an unexpected marker or invalid original registration. It must be run in a disposable profile with no normal saves. Do not save the experiment.

Build from the repository root:

```powershell
dotnet build Research/MilepostProbe/MilepostProbe.csproj -p:GameManaged='D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='C:/TMP/MilepostProbe18/obj/' -o C:/TMP/MilepostProbe18/out
```

Stop the game before installing the DLL and `Info.json` into a separate disposable profile. Enable it there, start the exact fixture and let its origin settle. With no other active mutation, click `Inspect T-46 rebind and T-47 origin` once. The action snapshots the marker registrations, text fields and origin lookup, temporarily changes the existing T-46 marker's distance on its current segment, then restores and checks the original binding and registration. It restores the T origin-cache entry to its captured state in a `finally` block. The action does not change model, mileage or text fields. Preserve `MilepostProbe.log`, inspect the baseline, rebound, restored and final verification lines, and exit without saving. If an error or restoration failure occurs, abandon the disposable session without saving.

The logged text field is not a rendered-text observation. The case does not establish different-group rebinding, train consumers, persistence or behavior across reloads. The source snapshot named by DLL hash identifies the executed build; an archived binary is not required in the repository.
