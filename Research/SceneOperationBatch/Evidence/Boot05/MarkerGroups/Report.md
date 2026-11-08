<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Marker groups: Boot05 local placement trial

This report records a manual T-47 placement trial and an automatic editor reentry attempt in the scene-operations fixture. The [authored probe log](MarkerGroupsProbe.txt) records both. Boot05 did not attempt placement in an ordinary save; [Boot06](../../Boot06/MarkerGroups/Report.md) records a later trial.

The operator observed the white T-47 post at approximately screen position `(1287, 830)` at baseline. After application it appeared at approximately `(1478, 880)` with a changed orientation. The repeat appeared stable. After restoration the post appeared back at its original position. These are visual observations of this trial, not measurements of world displacement or proof of persistence.

The probe used marker `Mnau` on `Stky0cg9c` in `sy-db` and resolved a destination one metre farther on the same segment and end. Application and repeat returned `true`. The restored binding had one matching registration and one native group subscription observing `sy-db`. This local trial did not repeat the cross-group or explicit-disable tests in [Boot02](../../Boot02/MarkerGroups/Report.md) and [Boot03](../../Boot03/MarkerGroups/Report.md).

Native restoration returned `true` and recorded `LastRuntimeApplySucceeded=True`. Before writing the captured pose, the probe logged world position `(405.76170, 611.87240, 464.05470)`, a position difference of `0.1389771` metres from the captured pose, and zero rotation difference at the logged precision. The probe then wrote the captured root pose and logged zero position, rotation, local-position and local-rotation differences. It restored the original nullable last-apply value, which was `null`. Native success therefore did not by itself reproduce the captured position exactly. The checks do not establish restoration of every dependent object or cache.

## Automatic editor reentry attempt

On editor reentry, world generation 7 requested T-47 focus and logged five seconds of stable origin with the expected map. Capture then rejected the attempt because it found no active visible renderer. The probe attempted no mutation or retry. A subsequent screenshot showed the native startup camera at Wilmot station rather than T-47. The startup camera may have overridden the focus request, but the log and screenshot do not establish that cause. This attempt does not demonstrate automatic local placement. The earlier [Boot03 automatic application](../../Boot03/MarkerGroups/Report.md) remains evidence for its own fixture load and guards; the new visibility guard is not a universal readiness signal.

## Native failure conditions

Read-only Mono.Cecil inspection of the installed `Railroader.dll` with SHA-256 `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E` found that `ConfigureAndApply` stores the requested settings, calls `TryApply` and records its result. `TryApply` returns false for an absent marker, missing marker `PositionRotation`, or missing target. When terrain snapping is enabled, `MoveTransform` also returns false if its terrain-layer raycast misses. The ray starts ten metres above the computed placement and travels down for twenty metres. A miss returns before native pose writes. When group following is enabled during play, `TryApply` reobserves the group after the placement attempt; the absent-marker path returns earlier. Exceptions are separate from a false return.

The local visual probe requires a matching terrain ray hit at both locations even with snapping disabled. This is a stricter trial precondition than native placement. The [Boot03 report](../../Boot03/MarkerGroups/Report.md) did not record the native pose or ray needed to distinguish the false-return paths. Distance and unavailable terrain are possible explanations, not established causes.

## Ordinary-save baseline

The operator selected Sandbox and the Scene operations research map, then created `CrankyAnt Scene Operations Trial`. The local native player log's `ActiveMapDefinition` entry for this load identifies `crankyant/scene-operations-map` with `editTarget false`. The clean save was archived locally as `CleanOrdinaryBaseline.railroad`, SHA-256 `78D3ED746EE47F954A4568EF5C71216D2DCAB7360FDCF3E64B2498069D3711BB`. No placement was attempted in this ordinary save during Boot05. This establishes a baseline for a later trial, not automatic application or persistence.

## Provenance and limits

The [build manifest](../BuildIdentity.json) identifies the prepared MarkerGroups DLL as SHA-256 `2EB87938042AFDD153727473938298F29CFA7CF2C4CF9DA493D2A0CDD2CC97B9` and the frozen [local trial source](../AuthoredSources/MarkerGroups/LocalPlacementTrial.cs.txt) as `C7924EAD290067E48B399F1B09BD6ABD364A65010B3C0D9492B8F5AF4C713066`. The manifest is build evidence, not independent verification of every loaded asset. The original local probe log SHA-256 is `600E3036CEDA932CDB857E2D7B276859A951ED892E25859A72D49B55C51CF13C`. The linked copy uses LF line endings and removes one trailing space from a stack-trace line; its SHA-256 is `716F5563C2C2330B356621686BE515535E749301BDBCEEDF3A3FF2BAE2BCD951`. The original native player log and save remain local.
