<!--
SPDX-FileCopyrightText: 2026 CrankyAnt
SPDX-License-Identifier: MIT
-->

# Broken marker probe

This is a disposable research experiment, not a patcher. The fixture layers `rr/bushnell-whittier`, deletes Shuo with the same null delta used by the earlier inventory experiment, and adds a separate replacement segment. The deletion and the marker's resulting state must still be observed at runtime. This fixture has its own map identifier, `crankyant/broken-marker-test-map`.

Build from the repository root with the game installed at the stated path:

```powershell
dotnet build Research/BrokenMarkerProbe/BrokenMarkerProbe.csproj -p:GameManaged='D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='C:/TMP/BrokenMarkerProbe18/obj/' -o C:/TMP/BrokenMarkerProbe18/out
```

Use a separate disposable profile and the shared runtime lease. Install the fixture and reviewed probe there only. Do not install or use the earlier fixture with the same replacement segment in this profile. Never save this experiment. Exit the disposable session without saving; the probe does not restore the broken state.

1. Start without a file named `ARM_BROKEN_MARKER_PROBE` in the probe mod directory. Enter the fixture map. The automatic read-only preflight waits at most 60 seconds for the graph and marker, then logs the map, unresolved Shuo location, registration count, replacement endpoints, pose and activity. Stop on any error or if Shuo still resolves. Preserve the log and native warning evidence.
2. Exit without saving. With the game stopped, create an empty `ARM_BROKEN_MARKER_PROBE` file in the probe mod directory. Enter the same disposable map. The probe performs its preflight and attempts one automatic rebind per world load. Capture the log and screen. Exit without saving.
3. Re-enter the same map as a fresh world. Capture the new world instance preflight and result, and exit without saving. Remove the arm file with the game stopped. Do not treat a successful setter or warning disappearance alone as proof of consumer behavior.

The guard checks the active map identifier, unique M5pp component and path, original serialized location, missing Shuo, unresolved original location, replacement node IDs and positions, and a resolvable replacement location. It records any existing M5pp graph registration and refuses unrelated registration objects or duplicate entries. Armed repair additionally requires zero baseline registrations. The mutation disables the component, sets its location and reenables it. It checks the resulting location, serialized values, graph registration, origin, marker pose and activity. Errors stop retries for that world. Other marker consumers and visual children are not fully verified; inspect them separately. The preflight and setter can fail because lifecycle timing or native behavior differs from the assumptions. A failed postcondition may leave the disposable world changed, so exit without saving.
