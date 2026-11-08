<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Crossing pad Boot02

[Test crossing-pad geometry after marker changes](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/26) asks whether an applicable crossing-pad marker change updates generated geometry and culling and whether the original state can be restored. In this trial, a one-metre same-segment rebind changed the marker registration but left the existing generated mesh and culling sphere unchanged through the delayed observations. Calling the native `Generate` method explicitly then produced a different vertex signature and moved the culling sphere. Restoring the binding alone retained the changed mesh and sphere; explicitly generating again restored the recorded baseline signature and sphere. This is one rebind-only test, not a placement test or a guarantee for other pads.

## Identity and method

The disposable `crankyant/scene-operations-map` ran in the isolated RuntimeHome. The target was the unique Mg74 marker on `World/Roads Wilmot/Crossing Unprotected/Crossing Pad`, bound to Sj7c end A. The assembly was Railroader 0.6.2 experimental build 26335745; the recorded Railroader.dll SHA-256 is `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. This hash does not identify the scene assets. The active fixture's `Data/Map.json` SHA-256 was `36A99A8EBB6E2198C28D3C6D4AA2C859CE70B1F85E0D4E14243FA6837C9029AE` and matches the frozen Boot02 build manifest. The installed and frozen probe DLLs both hashed `C8B24771457BF130CAE7E713A40E80F3B99D9B7421CB96F9743ABB333B19E558`. The frozen and current probe source both hashed `72BDF2BEA646974207F8F616B919582354C6B4CD58BA2AB1604BADF350C22AC6`. The [batch build identity](../../../BuildIdentity.json) describes the staged build; the frozen Boot02 copy identifies this run.

The operator focused Mg74, waited, captured a baseline, rebound by +1 metre, captured a delayed snapshot, repeated the rebind and captured another delayed snapshot. The operator then called native Generate, observed the result, restored the original binding, generated again and verified restoration. The probe recorded one matching Sj7c graph registration at every snapshot and an unchanged floating-origin value. It did not invoke `TrackRelativePosition.TryApply`.

## Telemetry

The [authored probe log](CrossingPadProbe.txt) contains all 65 lines from the local probe log, with CRLF normalized to LF. The original SHA-256 is `10337B7A1F0DBD861EFE0EA8A85C12577116EE85B9F195D2719B52BE2F284978`; the retained SHA-256 is `F5676E23C098A651516F6E7D52448EF111A7269C56070D6DE2D3479F4FF5AF23`. Timestamps and decimal separators remain as logged.

| Phase | Binding distance | Mesh vertex signature | Culling sphere position |
| --- | --- | --- | --- |
| Baseline | 185.9477 | `E6846278CF9A927E` | `(510.47070, 569.77400, 295.20450)` |
| Rebind and delayed repeat | 186.9477 | `E6846278CF9A927E` | `(510.47070, 569.77400, 295.20450)` |
| Explicit Generate, delayed | 186.9477 | `42D2D42A024C5E96` | `(511.46290, 569.77400, 295.16760)` |
| Binding restored before Generate | 185.9477 | `42D2D42A024C5E96` | `(511.46290, 569.77400, 295.16760)` |
| Generate original and verify | 185.9477 | `E6846278CF9A927E` | `(510.47070, 569.77400, 295.20450)` |

The marker and target pose remained at the recorded baseline values, and the logged renderer was enabled. The pre-generation observations retained the same mesh instance as baseline. The second delayed rebind observation was approximately 35 seconds after the first rebind. The immediate snapshots after each explicit Generate list both the old and new mesh objects; the later snapshots list one. This is consistent with deferred destruction, but the log alone does not establish the destruction mechanism. The final mesh object had a different instance ID from baseline. The probe reported `contentAndSphereMatch=True` after restoration.

The signature is FNV-1a over local vertex bytes. Its comparison includes vertex count, mesh bounds, mesh transform and culling sphere, but not triangle indices, material, every renderer property or every dependency. A matching signature is not proof of identical complete geometry. The probe checks the original marker binding and registration separately. No model or mesh was edited by the probe; Generate is the game's native builder operation.

## Observation limits

The captured screenshots `CrossingBaseline.jpg`, `CrossingGenerated.jpg` and `CrossingRestored.jpg` remain local in the Boot02 visualization folder. Tree foliage substantially obscured the pad, so they do not establish detailed visible movement or visual restoration. No audio claim is made. The result does not establish automatic updates after other events, track-relative placement, reload persistence, arbitrary origin shifts, all culling behavior, or other crossing pads. The static builder path and correction to the earlier OnEnable description are recorded in the [preparation report](../../../CrossingPad/README.md).
