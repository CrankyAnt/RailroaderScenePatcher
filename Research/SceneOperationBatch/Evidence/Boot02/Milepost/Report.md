<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Milepost trial, Boot02

The T-46 origin marker was rebound from `Stvw` to T-47's valid location on `Stky0cg9c`, assigned the same alternate location again, and restored. During all four sampled phases the T prefix cache held the same T-46 object and T-47's `Origin` resolved that object. The sampled binding and registrations returned to baseline. The cache had been absent before capture; after the restored snapshot, cleanup verified its absence again. The [selected telemetry](Telemetry.md) records the values and the limits of these checks.

The operator observed the white T-47 post and its rendered `47` at baseline, rebound, repeat and restore from the same camera position. No visible change was observed. This is a visual observation separate from the synchronizer and TMP text telemetry. The screenshots remain local and are identified below by SHA-256; they are not included in the repository.

| Phase | Screenshot SHA-256 |
| --- | --- |
| Baseline | `5FD24F3DBF595736352988202016F198F49F9FF4B8624CEC1BE6504A623F61E7` |
| Rebound | `55B0BFEB4CF7C116161AE2FF22D70E9375296F1C438015DC1F5B5123A22C1A92` |
| Repeated | `7B927E4EFECF4DB3443C0103CDA2523771AB08B39FA130D36B9497716D8FA425` |
| Restored | `514B412ECE5EAAA27BF3E73894AB110E3DF1BD3A1E23B16E93FC01C1EFBF077E` |

The isolated map was `crankyant/scene-operations-map`. The frozen build manifest identifies the authored DLL as SHA-256 `4763FAEC02846D7CD2CF1DAB7B8C8A411E41D2675E7847CA7D7D04EFCC016B3D`; independently hashing both the frozen and installed DLL yielded that value. The frozen authored source SHA-256 was `828F22ADB12FE8999A7275488CD42FD7799D0156FF65A75524D391C04AE2338B`. The manifest identifies fixture `Info.json`, `Definitions.json`, and `Data/Map.json` respectively by SHA-256 `590D4B7A28C7343E1A50E30D101C172373BFC34797CD213AE261B2D63A151D08`, `0C34D360EBF250163A21448E21F2AEB5315D83BD7C6D8A3C4DB014D2F5C2446B`, and `36A99A8EBB6E2198C28D3C6D4AA2C859CE70B1F85E0D4E14243FA6837C9029AE`. The manifest itself calls its evidence build-only; the runtime observation is the separate probe log and visual record. The source and build identities do not identify all vendor scene assets.

The probe temporarily populated an originally absent cache entry. It did not test an entry that existed before the trial, other prefixes, a changed group, group toggles, another map, or reload and persistence. Group identity and observation state stayed `sy-db` in the samples; that is not proof of group subscription behavior under a group change. The probe checked sampled object, mesh, pose, activity, mileage and text fields; it did not verify every renderer material or all possible consumers. It did not alter the model or mileage fields. The test does not establish that native autosave did not occur.
