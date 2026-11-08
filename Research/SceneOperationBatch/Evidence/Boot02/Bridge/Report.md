<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Bridge trial, Boot02

On the isolated `crankyant/scene-operations-map` fixture, the Ela bridge root was moved 5 m and rotated approximately 15 degrees. Repeating the operation produced the same recorded root pose. The operator observed the bridge shift relative to the track, then disappear when its root was disabled and return to its original alignment after restoration. The screenshots remain local; their hashes appear below. The [selected telemetry](Telemetry.md) records the root pose, dependency checks and contact callbacks.

The disposable ball produced an `OnCollisionEnter` callback against the same bridge collider at baseline, at the moved pose and after restoration. Each trial also recorded a later contact with track or terrain. The ray used to place the ball is not itself a collision test. These samples do not establish contact across the rest of the bridge, under trains, or under other physics conditions.

The scenery component referenced segment `31lvmw`, group `ela-bridge`, with `GroupEnabled=True` and `ConstructionState=Complete`. Its two entries referenced the bridge's `Abutments` and `Steel` children, respectively with `NeedsAbutments` and `NeedsBridgeSteel` under `NoOverlap`. The recorded unconstructed state was `NotStartedBridgeSteel`. The recorded configuration, segment fields and target hierarchy identities matched baseline at every snapshot, including the disabled root and a delayed snapshot after restoration. The separately captured `Ela Bridge Scenery` controller had no child transforms in the baseline. Neither it nor its referenced targets identifies the timber approach visible in the earlier trial. That approach's actual owner, pose, activity, collider and culling state remain unverified. No construction state or group state was changed, so this run does not test their visibility transitions.

Read-only inspection of the installed `Railroader.dll` identifies a candidate for a later ownership check. `TrackObjectBuilder.CreateRoadbed` can call `CreateTrestle`, which creates a child named `trestle` with an `AutoTrestle.AutoTrestle` component. That component submits beam matrices to `Track.PrefabInstancer`; child renderer enumeration alone would not account for those instances. No Boot02 observation ties a particular `AutoTrestle` object or generated instance to the visible timber approach.

The immediate restoration check reported exact captured local transforms and activity, matching collider identities and recorded shape properties, and world poses within 0.01 m and 0.01 degrees. The delayed snapshot recorded the baseline root pose and matching dependency configuration; it did not rerun the aggregate restoration assertion. The checks cover the captured bridge and referenced target descendants, not every scene object or renderer property. They do not establish persistence, reload behavior, or that autosave did not occur.

| Phase | Screenshot SHA-256 |
| --- | --- |
| Baseline | `65F79E67629754ACD594000DF9FEA1ECF8BA3448FD321B47D5C00FC4B3C139EC` |
| Moved | `A45503B95BCB8FCCEFCD71B2C168FF1088A62B8C8DF5506E4D7F8AC3976E1B4B` |
| Disabled | `7AE5F960F0AB42893FD689CA4058DBF2039EC1811AC2F6AAE80D1E1FA796BEFA` |
| Restored | `2C919F0DE8634B5525A097222C412A1F427A1B96D6DCA9D1469F4A5F2C37AE75` |

The frozen and installed probe DLLs both hash to `2BC76294F9BA7DE17038AD8C15489E1CC38C5FEC8C670424DD438DB31ED2F3D9`, matching the Boot02 build manifest. The frozen `BridgeContactProbe.cs` and `BridgeSceneryState.cs` hash to `A336FA619D28480754D9BB5659530586A9D35A2B2369B2CF579AA1E8E178EF5C` and `A6188F1E5CBE2F33758E0E2043DFB7AF75195F93E38F08BAA4939ED8BFDDB536`. The manifest identifies fixture `Info.json`, `Definitions.json`, and `Data/Map.json` by hashes `590D4B7A28C7343E1A50E30D101C172373BFC34797CD213AE261B2D63A151D08`, `0C34D360EBF250163A21448E21F2AEB5315D83BD7C6D8A3C4DB014D2F5C2446B`, and `36A99A8EBB6E2198C28D3C6D4AA2C859CE70B1F85E0D4E14243FA6837C9029AE`. The manifest describes build evidence only; the probe log and screenshots are separate runtime evidence. These hashes do not identify every vendor scene asset.
