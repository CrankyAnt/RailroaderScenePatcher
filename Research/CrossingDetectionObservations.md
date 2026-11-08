<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Additional crossing detection observations

This runtime record supports [Test additional crossing detection and controller membership](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/21). Construction source, fixture and explicit span inputs are retained beside this record. Train passages, physical observations and controller samples are separate evidence. [AdditionalCrossingDetection.md](AdditionalCrossingDetection.md) states the bounded feasibility result.

## First added-track passage

The ordinary sandbox uses `crankyant/crossing-detection-research` and the independently authored `SCrankyAntDetection21` track. The probe appended the three spans `CrankyAntDetection21_A`, `CrankyAntDetection21_I` and `CrankyAntDetection21_B` to the existing Bryson Everett controller. Construction reported valid endpoints, nonempty computed coverage and exact graph registrations. The computed lengths were 241.9996, 16.00002 and 241.9998 native units. Original span identity, endpoints, membership and registration checks passed throughout the samples below.

The first passage ran from added approach B through the added island to added approach A. The six original spans remained unoccupied in every recorded sample. Samples run approximately every 0.5 seconds. Times below retain the probe log's UTC format.

| UTC time on 2026-11-08 | Added occupied spans | Controller lights / gates / bell |
| --- | --- | --- |
| 16:12:52.5548896Z | B | false / false / false |
| 16:14:06.3585783Z | B | true / false / true |
| 16:14:11.3928494Z | B | true / true / true |
| 16:14:29.0773577Z | B and island | true / true / true |
| 16:14:33.1283875Z | island and A | true / true / true |
| 16:14:37.1781280Z | A | true / true / true |
| 16:14:37.6793161Z | A | false / false / false |

After the passage, the operator reported that the approaching locomotive caused the gates to close abruptly with audible bells. The gates then opened and closed again while the train passed. After the train passed, they opened gradually and the bells stopped. The locomotive stopped after this one outward passage. A later clarification confirmed that the operator had a clear view of both gates during the anomaly. Lamp flashing was not explicitly reported.

The sampled gate output stayed true between 16:14:11.3928494Z and 16:14:37.1781280Z. These samples do not corroborate a second controller gate-output cycle during that interval. They cannot exclude transitions between samples, a physical equipment or animation effect, or an observation whose timing is not individually recorded. The cause of the reported physical reopening and reclosing remains unresolved. Do not equate controller output with physical gate movement.

At 16:15:24.9854082Z, added approach A remained occupied, while all other spans and all three controller outputs were false. The locomotive had not cleared every monitored span. This is unsuitable for removal, although a controlled reverse passage can start from that stopped position. No removal or fresh-world reproduction is established by this record.

The first-run samples and non-sample diagnostics are retained with reconstruction hashes in [FirstRun.provenance.json](Evidence/FirstRun.provenance.json). Physical observations come from the live operator reports. The following sections record the later passages, removal and fresh-world construction.

## Return added-track passage

The return passage ran from approach A through the added island to approach B. All six original spans remained unoccupied in the recorded return samples. Identity, membership, endpoint and graph-registration checks continued to pass. The probe recorded this sequence:

| UTC time on 2026-11-08 | Added occupied spans | Controller lights / gates / bell |
| --- | --- | --- |
| 16:16:32.6912669Z | A | true / false / true |
| 16:16:33.7072613Z | A | true / true / true |
| 16:16:42.8551530Z | A and island | true / true / true |
| 16:16:46.9213911Z | A, island and B | true / true / true |
| 16:16:47.4221031Z | island and B | true / true / true |
| 16:16:51.4545073Z | B | true / true / true |
| 16:16:51.9734367Z | B | false / false / false |

There was one sampled activation and one sampled deactivation of each controller output. No sampled reopening occurred during island occupancy. The operator reported normal behavior on this return: the gates lowered gradually, remained closed while the train passed, and then opened gradually. The operator described the bells and gates as behaving normally, without the abrupt closing and reopening reported on the outward passage. Physical lamp flashes were not separately confirmed.

At 16:17:58.8352020Z, approach B remained occupied and every other span and all three outputs were false. The locomotive had stopped inside a monitored approach, so removal required further clearance. The return result demonstrates detection on this added track in the opposite direction, with the reported normal physical response. It does not explain or invalidate the differing outward observation.

## Clearance after the added-track passages

The operator moved the locomotive farther away from the crossing after the return passage. At 16:19:50.1483785Z, all nine monitored spans reported false and all three controller outputs were false. The operator then confirmed that the locomotive had stopped. This establishes sampled clearance after the two added-track passages. The added detection remains installed for the original-track control.

## Original-track controls with the addition present

The operator placed the locomotive on an original track. From 16:21:07.4598762Z, original approach `Pws9` reported occupancy while the three added spans stayed clear. The outward control then traversed `Pws9`, island `Pipr` and opposite approach `Pcyi`. The sampled lights and bell outputs activated at 16:22:21.6094048Z; the gate output activated at 16:22:27.1259837Z. Island occupancy began at 16:22:46.3447745Z and ended by 16:22:50.8783696Z. All outputs deactivated at 16:22:51.3957027Z. Every span was clear by 16:24:08.0599283Z.

The reverse control traversed `Pcyi`, `Pipr` and `Pws9`. Lights and bell activated at 16:25:18.1382891Z; gates activated at 16:25:23.6550775Z. Island occupancy began at 16:25:43.3252478Z and ended by 16:25:45.8412643Z. All outputs deactivated at 16:25:46.3585585Z. Every span was clear by 16:27:21.9927401Z. The added spans stayed unoccupied throughout both control passages, and original identity, endpoint, membership and registration checks passed. Each control had one sampled output activation and deactivation.

The operator reported expected gate and bell behavior on both original-track passages. On the outward control, the operator also reported the locomotive's expected whistle use and recalled that it might have been absent on the earlier added-track passage. This is an operator comparison with uncertain earlier recall, not a measured whistle test or a causal finding. The independently authored added-track fixture contains graph nodes and one segment; it creates no whistle marker or crossing-pad marker.

## First removal

The operator confirmed a stop after the reverse original-track control. Consecutive samples still showed every span clear and every output false. At 16:28:28.2313943Z, explicit removal completed. The probe verified its own registration identities before disabling its objects, removed its exact additional circuit set, verified that the three added graph registrations were absent, and rechecked the original two sets and six spans against their captured identity, endpoint and registration baseline. It then requested destruction of its own parent object. This demonstrates membership and graph-registration removal for this trial; it does not establish a universal rollback guarantee or independently inspect deferred destruction after the frame.

## Targeted first-activation follow-up

The first added-track passage prompted a targeted repeat after a full process restart. The 0.5-second controller samples alone could not establish whether a shorter output transition occurred or how the physical animation moved. The repeat added read-only physical gate-pose observations and per-frame controller-output change detection. Starting inside an approach does not by itself explain the anomaly, because the normal added-track return also started inside an approach.

## Fresh-world construction and first-activation repeat

The repeat used a separate disposable archive containing only the original clean manual snapshot, with no autosave payloads, after a full process restart. The same fixture and explicit span inputs were used. All three spans again resolved, calculated nonempty coverage, registered as the exact new objects and joined one new controller set. The original two sets and six spans again passed the captured-state checks. Construction completed at `2026-11-08T16:35:35.8131535Z`. This reproduces construction from declared inputs after world replacement; it does not demonstrate automatic application or persistence of runtime-created components in save data.

The first repeated passage entered approach B, island and approach A. All six original spans remained clear. The operator again observed both gates snapping closed, then opening and reclosing at normal animation speed. This time the operator explicitly confirmed the lights and bells during the anomaly.

At frame 18823, `2026-11-08T16:38:42.6586673Z`, both observed gate arms moved approximately 79.63409 degrees within one frame. Lights and bell became true while gates remained false. Both arms then returned gradually to their original raised rotation by `16:38:47.3346501Z`. Gates became true at `16:38:47.9257743Z`, followed by normal closing. Outputs returned false after island clearance at `16:39:12.1291168Z`, and the arms opened normally. All nine spans were clear by `16:39:57.5036798Z`. After confirmed standstill, the addition was removed again at `16:43:13.5507710Z`, with absent added registrations and retained original references and registrations. [GateRepeat.provenance.json](Evidence/GateRepeat.provenance.json) identifies the complete repeat evidence.

## First activation of the original circuit without an addition

A further fresh-process control used another copy of the same clean manual snapshot. The probe captured the original state and read both gates, but did not add any circuit or span. The operator placed the locomotive on an original base-map track outside the original approaches and confirmed the same physical anomaly on its first passage. This control still used the custom fixture and read-only probe.

At frame 15371, `2026-11-08T16:50:45.1205382Z`, both arms moved approximately 79.63409 degrees. The previous frame had no `gates` key, which read null/false, and playable time `-213.3647` with speed `-1`. The movement frame had a present false key and playable time equal to clip length `4.958333`, still at speed `-1`. Lights and bell were true and gates remained false. The later gates=true transition at `16:50:50.2984682Z` started normal closing. All outputs returned false after passage at `16:51:13.0696915Z`. All six original spans were confirmed clear before the operator confirmed a stop.

The original crossing therefore reproduces the anomaly without a new detection circuit. The observed key and animation changes match the [inspected initialization mechanism](GateInitialization.md); the exact intra-frame callback was not intercepted. [NativeControl.provenance.json](Evidence/NativeControl.provenance.json) identifies the complete control evidence. A final ordinary-base-map run without any probe or fixture is separate verification and remains pending.
