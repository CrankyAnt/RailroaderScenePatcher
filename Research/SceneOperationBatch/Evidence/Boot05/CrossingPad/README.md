<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Crossing pad Boot05

This trial repeats the Mg74 same-segment rebind from [Boot02](../../Boot02/CrossingPad/README.md) with both crossings visible. The operator positioned the camera before capture. The front pad appeared unchanged after the +1 metre rebind and delayed observation. After native `Generate`, it shifted along the track relative to the fixed road; the rear pad appeared unchanged. Restoring the binding alone left the front pad displaced. Generating again returned its visible alignment. The camera moved slightly after the restoration Generate during verification, so the final image is not an identical-camera comparison.

The [authored probe log](CrossingPadProbe.txt) contains 53 lines, with CRLF normalized to LF. Its original SHA-256 is `C4174E568ABE78988022AC8A74BEA67C72CF05BC958D0AB4897994E913932675`; the retained SHA-256 is `9D0EA17D4BA7E8822865CF2AB1A529711C71A851B682B7E6BF4F2EB3500F418A`. Timestamps and decimal separators remain as logged. The [frozen source](../AuthoredSources/CrossingPad/CrossingPadProbe.cs.txt) and [build identity](../BuildIdentity.json) accompany this run.

| Phase | Binding distance | Vertex signature | Culling sphere position |
| --- | --- | --- | --- |
| Baseline | 185.9477 | `E6846278CF9A927E` | `(510.47070, 569.77400, 295.20450)` |
| Rebind and delayed observation | 186.9477 | `E6846278CF9A927E` | `(510.47070, 569.77400, 295.20450)` |
| Generate and delayed observation | 186.9477 | `42D2D42A024C5E96` | `(511.46290, 569.77400, 295.16760)` |
| Binding restored before Generate | 185.9477 | `42D2D42A024C5E96` | `(511.46290, 569.77400, 295.16760)` |
| Generate original and verify | 185.9477 | `E6846278CF9A927E` | `(510.47070, 569.77400, 295.20450)` |

The delayed rebind snapshot was about 17 seconds after rebind. Each snapshot recorded one matching Sj7c registration and the same origin. The immediate Generate snapshots include both old and new mesh objects; later snapshots contain one. At 00:30:12 CET on 9 November 2026 the probe reported `contentAndSphereMatch=True` after checking the original binding. Its FNV-1a vertex signature and comparison also cover vertex count, bounds, mesh transform and culling sphere, but exclude triangle indices, material and other dependencies. The probe did not call `TrackRelativePosition.TryApply`.

Local visual artifacts are `CrossingBaseline.jpg`, `CrossingReboundDelayed.jpg`, `CrossingGeneratedDelayed.jpg`, `CrossingBindingRestoredBeforeGenerate.jpg` and `CrossingRestoredVerified.jpg` in the Boot05 visualization folder. They are not included in the repository. This trial does not establish reload persistence, behavior after other events or origin shifts, placement, all culling behavior, or results for other pads.
