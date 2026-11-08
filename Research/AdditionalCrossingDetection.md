<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Feasibility of adding track detection to an existing crossing

This report answers [issue 21](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/21): can an existing protected crossing detect trains on an additional track by adding a circuit set and spans?

## Conclusion

The examined Bryson Everett crossing supports a bounded feasibility conclusion: an additional circuit set with three track spans can detect passages on an additional track in both directions while the original-track control continues to work in both directions. The new set was removed successfully, and the same construction succeeded in a fresh world after a full process restart. This establishes feasibility for this controller, graph, fixture, input geometry and installed game build. It does not establish a universal three-span minimum, support for wholly new crossings, or safe timing across other layouts. Both original sets retained their identities and bindings; controlled train passages exercised set 0, not every original circuit independently.

The experiment also reproduced an abrupt first-activation gate movement on the original crossing during a later original-track control with no added circuit. A final fresh-process, ordinary-base-map control reproduced the same physical sequence with neither probe nor fixture installed in the active test folders. Thus the addition, probe and fixture are not required for the observed movement. The proposed gate callback cause remains an inference from matching instrumented frame observations and inspected code, not a directly intercepted callback.

## Examined setup

The inspected game was Railroader 0.6.2 experimental, build 26335745. `Railroader.dll` SHA-256 was `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. [BuildIdentity.json](Evidence/BuildIdentity.json) records the probe builds and evidence boundaries.

The target was the existing `World/Large Scenery/Bryson/Grade Crossing - Bryson Everett` controller. Its original state held two circuit sets and six distinct spans: set 0 used `Pcyi`, `Pipr` and `Pws9`; set 1 used `Pdlh`, `P6id` and `P1db`. The probe captured and checked original set order, object references, endpoint values and graph registrations during the trials.

The independently authored fixture was `crankyant/crossing-detection-research`, with track segment `SCrankyAntDetection21`. It provided the additional track graph for this test. The three added spans used unique IDs and the following explicit input ranges, each on end A of that segment:

| Role | Span ID | Input distances | Computed length |
| --- | --- | --- | ---: |
| Approach A | `CrankyAntDetection21_A` | 308 to 550 | 241.9996 |
| Island | `CrankyAntDetection21_I` | 292 to 308 | 16.00002 |
| Approach B | `CrankyAntDetection21_B` | 292 to 50 | 241.9998 |

The reversed input ordering for approach B is retained as supplied to the game's span input. The probe checked endpoint validity, nonempty computed coverage, and exact graph registration identities before exercising the circuit. The static construction model and its cache and registration caveats are documented in [CrossingConstruction.md](CrossingConstruction.md). The exact inputs are in [spans.json](CrossingDetectionProbe/spans.json), and fixture data is under [CrossingDetectionFixture](CrossingDetectionFixture/).

## Runtime observations

With the additional set installed, the train traversed the added route in both directions. In each passage the corresponding added spans reported occupancy, while all six original spans remained clear. Controller lights, gates and bell outputs activated and deactivated in each direction. The reverse passage had one sampled activation and deactivation, and the operator reported gradual gate movement and normal bell behavior. During the outward passage, the operator reported abrupt gate closure followed by reopening and reclosing while the train passed. The approximately 0.5-second circuit samples did not show an intermediate gates-output deactivation, so they cannot establish a controller-output cycle or explain the reported physical motion.

With the added set still present, a train traversed the original route in both directions. The original approach and island spans reported occupancy, the added spans stayed clear, and each control had one sampled output activation and deactivation. The operator reported expected gate and bell behavior. This supports preservation of the original route's detection in this setup; it is not evidence about other crossing layouts.

The operator then confirmed a stop, and consecutive probe samples showed every span clear and all outputs false. Explicit removal deleted the exact added set, verified absence of the three added graph registrations, and rechecked the original two sets and six spans against their captured identity, endpoint and registration baseline. This demonstrates successful cleanup for the trial. It does not prove a universal rollback guarantee or independently verify deferred object destruction after the frame.

After a full process restart, construction succeeded again with the same inputs. The first-added-track passage anomaly recurred. This is a fresh-world construction reproduction, not evidence that the anomaly is caused by construction or that the whole trial was repeated independently.

## Gate movement anomaly

The read-only gate observer also captured the same approximately 79.634-degree, one-frame movement of both raised arms during an original-track control with no added detection circuit in that boot. At the preceding frame, the controller's `gates` key was absent and therefore read false; at the movement frame, the key was present with explicit false. Lights and bell were true while gates remained false. The playable time changed from `-213.3647` to `4.958333`, the clip length, with speed `-1`.

Inspected code provides a plausible explanation: the controller writes all three output values when one changes, so its first explicit false gates write can notify observers even though an absent key already reads false. The animator handles an out-of-range playable time during a property change by resetting it to the opposite target. The recorded values match this path. The observer records frame state, not callback execution or intra-frame ordering, so it does not prove the exact cause. See [GateInitialization.md](GateInitialization.md) for the method-level evidence and remaining uncertainty.

## Evidence and limits

The retained evidence consists of the [first-run CSV](Evidence/FirstRun.csv), [diagnostics](Evidence/FirstRun.diagnostics.log) and [provenance](Evidence/FirstRun.provenance.json); the [gate-repeat CSV](Evidence/GateRepeat.csv), [diagnostics](Evidence/GateRepeat.diagnostics.log) and [provenance](Evidence/GateRepeat.provenance.json); and the [original-circuit control CSV](Evidence/NativeControl.csv), [diagnostics](Evidence/NativeControl.diagnostics.log) and [provenance](Evidence/NativeControl.provenance.json). Every source sample is retained. Source line numbers in the CSV and diagnostics preserve order and permit exact reconstruction of each normalized source log. [ExportDetectionEvidence.ps1](Evidence/ExportDetectionEvidence.ps1) verifies the reconstructed SHA-256 against the input. Provenance records source and derived hashes and row counts, including the original pre-normalization raw hash for the first run. Duplicate full log files are unnecessary because the exports reconstruct them without loss.

Physical gate behavior comes from operator observation, not the probe's controller-output values. Samples can miss transitions between their intervals. Arm rotation deltas are not calibrated open/closed states. The fixture adds one graph segment and no whistle or crossing-pad marker, and the research did not add or replace stock crossing equipment. No conclusion is made about whistle behavior, new equipment construction, safety timing, general rollback, or support for arbitrary track geometry.

The detection trials used a custom fixture and probe. A separate final control used a new ordinary sandbox on `rr/bushnell-whittier` after a full process restart with empty active Mods and ModDev directories. The operator reported both gates snapping closed, opening gradually and then closing again, with lights and bell active. This uninstrumented observation establishes reproduction without the research packs. It supplies no frame, key, playable or occupancy measurements and does not directly prove the inferred callback mechanism. See the [final control record](CrossingDetectionObservations.md#uninstrumented-base-map-control).

## Consequences for the issue map

Issue 21's bounded question is answered for the examined topology. This finding does not close the broader work in issue 18, which already owns general editor lifecycle, automatic application, origin tracking, cross-version compatibility and failure handling. Those remain separate work. The observed first-activation anomaly and proposed mechanism should be carried as compatibility and initialization evidence for that existing scope, not treated as a new implementation ticket or as proof of universal behavior.

This report records feasibility only. It does not define a production implementation, schema, work brief, or general authoring identity model.
