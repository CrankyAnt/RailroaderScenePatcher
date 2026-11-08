<!--
SPDX-FileCopyrightText: 2026 CrankyAnt
SPDX-License-Identifier: MIT
-->

# Crossing equipment trial observations

This record supports [issue 22](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/22). The evidence is a snapshot of the independently authored probe log, [EquipmentTrial.log](Evidence/EquipmentTrial.log). Line endings are normalized to LF in the retained artifact. It contains 365,091 bytes, SHA-256 `AEB14D2E68E42326DA935A01A98B5B05F28D8AE0E9EB22FF8EDCED51E69FB79F`, ending at the completed `pause` command at `2026-11-08T17:56:11.8820643Z`. Later log entries are outside this snapshot. The log records probe module IDs `7c814d42-ad00-4f2e-a80a-1d7196a219c3` and `0621a8ba-fad0-4a45-a1bc-70f9271909dd`. Timestamps below retain the log's UTC format.

## Construction and observed equipment

At Bryson Everett, the probe cloned Signal 1 with a world offset of `(5,0,0)`, shared its gate animation clip, remapped targets and left the culler entries unchanged. It reported zero gate-hierarchy colliders. It separately added a bell using the original bell as source, with zero colliders. See the `EQUIPMENT added` and `BELL added` entries near `17:50:37` and `17:50:46`.

The initial trials produced 119 logged `EQUIPMENT frame` samples, containing 55 `lights=False gates=False`, 53 `lights=True gates=False`, and 11 `lights=True gates=True` observations. Across these recorded samples, the formatted original and clone gate-local quaternions and lamp color strings match, with both observers reported present. These are samples, not every rendered frame. For example, at frame 5896 both gate rotations were `(-0.70598, 0.00000, 0.00000, 0.70823)` and the logged lamp color strings matched. The values represent sampled material output and local pose; they do not alone establish what a player saw.

The user reported that all three gate installations reacted the same in lamp and animation, and that the expected, known base-game first-activation bug occurred. This is a live observation, separate from the two-object probe comparison. The user also reported that the train stopped past the crossing. The probe did not record occupancy telemetry, so that observation does not establish occupancy or a cause for release.

At `17:52:55`, an explicit sample recorded lights, gates and bell as false and matching original and clone gate quaternions `(-0.99591, 0.00000, 0.00000, 0.09034)`. It listed nine material mappings per lamp observer, all with zero `_EmissionColor` at that sample.

## Reload and reconstruction

After a same-process world unload and reload from a clean manual save, the probe reconstructed the equipment at `17:55:05` and the bell at `17:55:13`. The log records the same source and added paths, offset, gate clip and bell definition as the earlier construction. The equipment sample at frame 18559 reports both lamp and gate observers present, matching zero lamp colors and matching local gate quaternions. The bell sample reports both players stopped. This establishes reconstruction and observer initialization for the reloaded world; no second train activation was tested.

Across the full snapshot, including this reload, there are 120 logged equipment samples: 56 with both outputs false, 53 with lights true and gates false, and 11 with both true. All formatted original and clone lamp color strings and gate quaternions match. The reload adds one inactive sample to the initial 119. A case-insensitive search for `error`, `exception` and `failed` found no matching log entries.

The equipment removal verification at `17:55:34` again reported the wrapper absent and source, culler and spans intact. The bell removal verification at `17:55:54` again reported the original player and asset references intact.

## Bell and removal

At frame 5893 the bell signal, original player and added player changed to true. The periodic sample at `17:51:27` reports two playing virtual sources for each bell, including a source at time `0,1943537` for each. At frame 7505 the signal and both players were false. The periodic sample at `17:51:54` reports the stopped sources. Audio times use the log's decimal-comma culture. The user heard a bell, but the trial did not isolate or record the added bell's audibility.

The probe verified equipment removal twice. Its verification reported the wrapper absent and the source, culler and spans intact at `17:48:36` and `17:53:18`. Bell removal was requested at `17:53:27` and verified at `17:53:36`; the probe reported original player and asset references intact. These checks cover the inspected references and objects, not all possible side effects.
