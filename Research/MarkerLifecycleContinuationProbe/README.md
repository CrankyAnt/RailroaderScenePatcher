<!--
SPDX-FileCopyrightText: 2026 CrankyAnt
SPDX-License-Identifier: MIT
-->
# Marker lifecycle continuation probe

This experimental probe currently records lifecycle observations only. It does not rebind markers, apply placement, change groups, or test restoration. The fixture contains the existing scene-operations replacement segment with group `CrankyAntIssue18Group` for a later controlled trial. Do not activate it alongside another pack defining `crankyant/scene-operations-map`.

The trace subscribes during `OnModLoad` to `MapWillLoadEvent` and `MapDidLoadEvent` through the disposable event-bus overload and records subscription errors. It also records `OnWorldLoad` and `OnWorldUnload`, map and graph availability, origin, matching marker identities and diagnostics containing `marker` or `location`. For each loaded Mdow or M5pp marker it logs its serialized binding, optional cache and invalid-warning fields, and whether the graph resolves that binding. It checks for `SCrankyAntSceneProbe` and resolves a separate serialized candidate at end A, 50 metres, without changing the marker. These checks also work on the broken-marker map when its graph and marker are available. Absence of a marker or graph is reported as unavailable, not as a failed resolution.

The diagnostic count begins at `OnModLoad` and is not a complete warning history. A missing event or diagnostic does not establish that it never occurred. Event subscriptions succeeded in the recorded Boot03 and Boot04 trials; their availability observations are in [ApplicableOperationsContinuation.md](../ApplicableOperationsContinuation.md). The static inspection and assembly provenance are in [ApplicableLifecycleStatic.md](../ApplicableLifecycleStatic.md).

Build from the repository root with the game stopped or running:

```powershell
dotnet build Research/MarkerLifecycleContinuationProbe/MarkerLifecycleContinuationProbe.csproj -p:GameManaged='D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='C:/TMP/RailroaderIssue18MarkerBuild/obj/' -o C:/TMP/RailroaderIssue18MarkerBuild/out
```

For an isolated observation, the user must already have enabled the disposable test home's unsafe DLL setting. Install the reviewed DLL and `Info.json` only in that home, and restart the game. Load the intended fixture or broken-marker test map and retain the probe log and game log. Verify the process command line identifies the isolated home. The panel's button records an additional snapshot; it does not alter the scene. Stop the game and remove the test pack after observation. Do not use a personal save.

The intended later group-follow experiment still needs code to capture and restore the binding, registration, placement configuration, callback, observed group and enabled states. Its restoration and behavior need review and an isolated runtime trial before drawing conclusions.
