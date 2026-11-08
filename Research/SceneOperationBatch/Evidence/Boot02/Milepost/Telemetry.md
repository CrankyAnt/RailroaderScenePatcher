<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Milepost trial selected telemetry

The selected values below come from `MilepostProbe25.log`, SHA-256 `0CE7C7C75F82DC05D9DB8FBF081E668CB78578A1A4B9CC4734045EDA3D2A09A4` at capture. Instance IDs apply only to this process. Decimal commas in the original log are normalized to decimal points. The table is a selection, not a copy of the complete log.

| Phase | T cache and dependent origin | Mdow binding | Mdow registration | Mnau registration |
| --- | --- | --- | --- | --- |
| Baseline | 259584 / 259584 | Stvw, B 51.20403 | Stvw | Stky0cg9c |
| Rebound | 259584 / 259584 | Stky0cg9c, B 266.9192 | Stky0cg9c | Stky0cg9c |
| Repeated | 259584 / 259584 | Stky0cg9c, B 266.9192 | Stky0cg9c | Stky0cg9c |
| Restored | 259584 / 259584 | Stvw, B 51.20403 | Stvw | Stky0cg9c |

The pretrial T cache entry was absent. Each phase reported `sameOrigin=True`. After the restored snapshot, the probe reported `final prior cache presence and identity restored; binding and registrations checked`. For the absent initial entry, this means the probe removed the entry it populated. It does not mean the entry stayed populated after cleanup.

All four phases recorded T-46 post ID 259584, marker ID 260296, mileage 46, synchronizer text 46 and three TMP text values of 46. T-47 recorded post ID 259602, marker ID 260322, mileage 47, synchronizer text 47 and three TMP text values of 47. Both relative-position components reported `follows=True`, `observing=True`, group `sy-db` enabled, and the same target IDs, 226664 and 226638 respectively. The serialized `post=` rows for each post were identical across all four snapshots after removing their timestamps and phase names. Those rows contain the sampled poses, object and mesh identities and activity. Registration checks verified one entry per marker, matching marker object, location and segment bucket. No `ERROR` or `ABANDON` line appeared in the captured log.
