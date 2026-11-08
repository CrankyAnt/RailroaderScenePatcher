<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Crossing consumer probe

This diagnostic is prepared for source review. Do not install or run it before review. Use only a disposable world and never save it. The button checks that the active map is `crankyant/scene-operations-map`, and checks the unique, active Pipr span under the Bryson Everett crossing, its graph registration and its expected endpoints. It calls `GetSegments` to establish a baseline, moves the lower endpoint by 0.25, calls `GetSegments` again, restores the endpoint in `finally`, and calls `GetSegments` once more. The log records cached point coordinates, segment IDs and length at each phase. The probe compares restored contents and length to the baseline and checks registration and endpoints again. Check the probe and game logs for errors. An exception or mismatch requires abandoning the session without saving.

Build from the repository root with the installed game assemblies and an external intermediate/output directory:

```powershell
dotnet build Research/CrossingConsumerProbe/CrossingConsumerProbe.csproj -p:GameManaged='<game>/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='<temporary-directory>/obj/' -p:OutputPath='<temporary-directory>/out/'
```

The call demonstrates only whether this cache rebuilds under the tested conditions. It does not establish that a train is detected or that lights, gates or bell operate. The later [crossing circuit experiment](../ApplicableSceneOperations.md#group-and-crossing-span-experiments) used a disposable sandbox with an SW1 and passages in both directions. That experiment records train detection, controller signals and observed gate movement separately from unverified light flashes and audio. Manual signal activation is not evidence of train detection.
