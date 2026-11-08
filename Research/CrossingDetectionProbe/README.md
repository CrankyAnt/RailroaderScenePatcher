<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Crossing reroute research probe

This experimental probe supports [Test correction of valid crossing detection after a track reroute](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/23). It adapts the [additional detection probe](https://github.com/CrankyAnt/RailroaderScenePatcher/tree/9495abf1fcef5f3c129a6ebd63a0af8a3c62604a/Research/CrossingDetectionProbe) and its authored track fixture. It targets the Bryson Everett controller and the `crankyant/crossing-detection-research` map. This is research code, not a patcher or a general rollback implementation.

`capture` records the two sets, six original spans, their registrations, geometry and selected controller and equipment references. It begins occupancy and controller sampling and a per-frame gate observer. `add` creates three spans on the fixture segment and replaces the three references in set 0, retaining both original sets and all original graph registrations. `remove` restores the original references, disables the owned spans, checks their registrations and requests destruction. `verify` checks the owned root after deferred destruction and rechecks the captured original state. The detached original spans remain sampled. `inspect`, `input`, `focus`, `overview`, `pause` and `sample` are diagnostic commands. The host also reads one command at a time from `command.txt`.

The current source requires the isolated RuntimeHome process argument and probe directory, the fixed target map and controller, `ARM_CROSSING_DETECTION`, a working gate observer, clear sampled spans and false controller outputs for mutations. These checks do not prove that all other physical track is clear, nor that a vehicle is stopped. Use only a disposable save and verify the vehicle and all affected track separately. Never save a mutated session. Explicitly remove and verify before unloading; guarded cleanup can refuse when state differs.

The source in `Snapshots/Trial01` identifies the version used in the first replacement and restoration test. That snapshot predates the home guard and the `verify` precondition fix. Its `Leaving` cleanup guard is ordered incorrectly, so that test required explicit removal and verification. The current source adds car location and velocity telemetry; it requires its own build and runtime verification. A successful build alone is not a runtime result.

Build with the installed managed references and outputs outside the repository:

```powershell
dotnet build Research/CrossingDetectionProbe/CrossingDetectionProbe.csproj `
  -p:GameManaged=D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed `
  -p:BaseIntermediateOutputPath=C:/TMP/RailroaderMapResearch/Issue23Build/obj/ `
  -p:OutputPath=C:/TMP/RailroaderMapResearch/Issue23Build/bin/
```

The track fixture and `spans.json` retain the earlier identifiers and geometry. They represent an alternative route for this experiment; they do not establish a new rerouted map or universal safe timing. Equipment output, sampled occupancy, physical gate motion, lamp illumination and audible bell are separate observations. See the [earlier detection result](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/9495abf1fcef5f3c129a6ebd63a0af8a3c62604a/Research/AdditionalCrossingDetection.md) and [equipment result](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/9c0403e487c98a0ac422ed67fa103e19d9e94be3/Research/CrossingEquipment.md) for their respective limits.
