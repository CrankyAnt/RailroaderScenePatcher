<!--
SPDX-FileCopyrightText: 2026 CrankyAnt
SPDX-License-Identifier: MIT
-->

# Applicable-object probe

This independently authored probe inspects and tests named objects only in a disposable profile and fixture. It is not a patcher. The current source includes read-only hierarchy inspection, camera focus, and guarded bridge and milepost pose/activity experiments. The exact sources of the earlier E094 bridge and AE4B milepost builds were not retained; see the research report for the observations and their limits. This source is a later variant and must not be presented as those exact builds.

Build the current variant from the repository root:

```powershell
dotnet build Research/ApplicableProbe/ApplicableProbe.csproj -p:GameManaged='D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='C:/TMP/ApplicableProbe18/obj/' -o C:/TMP/ApplicableProbe18/out
```

Use a separate disposable home, the specified fixture and a stopped game for installation. Review the source and build hash before use. A camera focus changes the view; wait for the floating origin to settle before capturing a baseline and do not move the camera during a captured operation. Capture, apply and restore separately. If a guard or restore fails, abandon the disposable session without saving. Log values describe the identified case and do not establish equivalent behavior for other objects.

The [next variant](../ApplicableProbeNext/README.md) adds the specific group and crossing-span cases used in the report. Preserve its exact source snapshot for each executed build.
