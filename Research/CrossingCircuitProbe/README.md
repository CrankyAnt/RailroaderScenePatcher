<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Crossing circuit probe

This disposable-save diagnostic requires source review before use. Build from the repository root with external output directories:

```powershell
dotnet build Research/CrossingCircuitProbe/CrossingCircuitProbe.csproj -p:GameManaged='<game>/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='<temporary-directory>/obj/' -p:OutputPath='<temporary-directory>/out/'
```

The operator installs the probe in the disposable home and places an `ARM_CROSSING_CIRCUIT_PROBE` file in its mod directory. The probe checks the exact railroad path, railroad name and map. Save the baseline before testing; do not save during the test. The focus button changes only the strategy camera. Capture starts baseline telemetry after validating the crossing and six distinct spans. The break button substitutes a nonexistent segment ID in the Pipr lower reference and invalidates only that span. The repair button restores the captured native location and rebuilds its cache. Both transitions require all six spans to report no cars. The probe attempts restoration if it leaves the world while the broken reference is active.

The log samples native `AnyCarsOnSpan` results, controller light, gate and bell values, the endpoints and world origin. Drive a real train through the crossing in each direction at baseline, broken and repaired stages; observe the crossing and audio separately. Match the timestamps and occupancy to those observations. A quiet sample or a manually activated signal alone does not demonstrate detection. If any guard, telemetry or restoration fails, abandon the disposable session without saving.

When Pipr is invalid, `AnyCarsOnSpan(Pipr)` returns false even if a train is physically on the island. The six-span transition check therefore cannot establish that the original island is clear during the broken stage. Before repair, the operator must visually confirm that the train is outside all original spans and check its native track position. A false invalid-span result is not clearance evidence.
