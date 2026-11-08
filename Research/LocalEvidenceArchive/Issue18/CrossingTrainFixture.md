# Protected crossing train fixture

Working note for issue 18. This is a read-only inspection of two disposable saves, not a train movement test.

## Fixture finding

Both files under `C:/TMP/RailroaderMapResearch/RuntimeHome/Saves/` are ZIP archives with a MessagePack `.rrs` member. A small in-memory decoder consumed each complete member and inspected the decoded maps. In both saves, `snapshot.cars`, `snapshot.carSets`, and `snapshot.trainCrews` are empty maps. The `snapshot.properties` entries with `engine` in their names are industry service and storage properties, not evidence of a locomotive. No saved locomotive position or route to the crossing can be derived from these empty car maps. Actual runtime operability was not tested.

- `scene-probe-clean-sandbox.railroad`: SHA-256 `50E397D32E05BE867555609D889F6AED6E112481A6D01D4AB07A17102438E1C1`; decoded member 62,155 bytes.
- `scene-probe-sandbox.railroad`: SHA-256 `1C3CC2061986BC99B77DB3E14EBA734EBC08BBBBBB7CEB3B8DC63530202EF539`; decoded member 61,640 bytes.

The decoding format and disposable-profile provenance are consistent with [Native save evidence](NativeSaveEvidence.md). Empty saved car maps do not prove what the game might generate after loading, but neither file establishes the requested train fixture. No normal save was inspected.

## Crossing and test prerequisite

The existing disposable probe log at `C:/TMP/RailroaderMapResearch/RuntimeHome/Mods/CrossingConsumerProbe/CrossingConsumerProbe.log` records a baseline single segment `Sz8t`, length 8.955988, with world points `(4391.33154, 529, 5377.95557)` and `(4382.91455, 529, 5374.89551)`. Its candidate changed the first point and its restoration recovered the baseline cache. The assignment identifies the target as `World/Large Scenery/Bryson/Grade Crossing - Bryson Everett`, Pipr island, `Sz8t`, A `0.1567912`, B `45.12718`. These points describe the crossing span, not a known locomotive location or a demonstrated approach route.

The missing prerequisite is a disposable, native-game fixture with a confirmed driveable locomotive on connected track with enough clear approach and stopping distance on each side of this crossing. Establish that fixture by ordinary in-game actions, identify the save and locomotive, and confirm both approaches and switch settings in game. This inspection did not establish an exact creation method, usable route, starting position, or native teleport operation. Do not assume that changing the two existing saves externally would create a valid locomotive.

## Proposed observation protocol

Once the fixture exists, use only that isolated profile and save. Record the save identity, locomotive, route, switch states, crossing identity, and unmodified endpoints. Confirm the locomotive can move and stop, and verify that both approaches are clear. Capture continuous video and audio showing the locomotive and crossing; include visible lights and gates and audible bell. Record whether each device exists and can be observed before interpreting its absence.

1. With baseline endpoints, approach from one side at a controlled speed, cross fully, stop clear, and record activation and clearing of each observable device. Repeat from the other side.
2. Apply the candidate endpoint repair by the separately established reversible probe, record the actual resulting endpoints and cache, and repeat both approaches under comparable conditions. Capture the actual train and device response throughout each pass.
3. Restore the original endpoints and cache, verify their values against baseline, and repeat both approaches. Preserve the isolated fixture and logs needed to reconcile any failure. Avoid saving probe mutations unless persistence is explicitly part of a separate test.

Record speed, direction, locomotive position at activation and clearing where observable, any errors, and incomplete observations. A manually forced alarm or a cache change alone is not evidence that an approaching train activates the crossing. This plan remains unexecuted; it does not establish that the repair affects train detection, lights, gates, or bell.

## Native route to provision the fixture

Static inspection of the installed `Railroader_Data/Managed/Railroader.dll`, SHA-256 `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`, identifies a direct native placement route. `Game.FeatureEnables.CanSpawnEquipment` is true in Sandbox or Debug mode. `UI.TopRightArea.ClickedPurchase` then opens `UI.Placer.PlacerWindow`; otherwise it opens the purchase window. The placer requires Trainmaster access. `UI.GameInput.Update` also maps its show-placer input to the same choice, but the binding was not established.

In a separate disposable Sandbox save with Trainmaster access, use the top-right purchase/placement control to open **Consist Placer**. Select the **Locomotives** filter, add an available visible locomotive with its plus control, and click **Place**. `PlaceConsist` calls `ConsistPlacer.Present`, which prompts the player to click track. Select clear, connected track on one approach to the Bryson Everett crossing. The placer builds descriptors from the selected definitions, including a tender where the selected definition requires one. It assigns an owned property; this does not establish that the engine is fueled, ready, or driveable. Confirm those conditions in game, identify the selected locomotive and its initial position, and check the connected route, switches, clearance, and stopping distances before attempting the passes above.

This route is inferred from static code and its displayed strings. The exact top-right icon, locomotive availability in this particular profile, placement validity near the crossing, controls, fuel state, and both approach routes remain untested. A placement failure or inability to drive is a test result, not permission to edit the save or infer a teleport capability. The console `SpawnCommand.Freight` selects freight cars and is not a demonstrated locomotive creation route. In Company mode the purchase window instead requires an eligible delivery interchange, valid resting location and sufficient funds, and schedules delivery; it is a longer alternative rather than evidence that either existing save already contains an engine.

## Native diesel movement controls

The observation recorded the upper-right engine-plus control opening Consist Placer; see [NativeConsistPlacer.png](NativeConsistPlacer.png). Static inspection of `ConsistPlacer.PlaceAt` shows that a newly placed consist passes `initialFuelWaterPercent` 1.0 and `PlaceTrainHandbrakes.Automatic` to `TrainController.PlaceTrain`. This is the requested initial percentage, not a runtime measurement of fuel or handbrake state. Confirm both in the game. Placement first calls `TrainController.CanPlaceAt`; do not bypass a rejected location.

The locomotive control adapter follows the selected locomotive. Select the placed diesel and choose manual controls if another mode is active. The options include **Simplified Controls**; disable it if the separate controls are not visible. `ManualControls` provides throttle, reverser, locomotive brake, and train brake. Its diesel reverser displays `F`, `N`, and `R`. With the locomotive stopped, set throttle to zero, select the intended direction, release the handbrake and relevant brakes, and increase throttle cautiously. To stop, return throttle to zero and apply the locomotive brake gradually. Wait for the speed display to reach zero before selecting the opposite direction. Test the brake response on clear track before the crossing pass and allow sufficient clear stopping distance beyond it in both directions. Verify switch alignment and that the full locomotive clears the crossing. These actions are a proposed runtime procedure; their actual operation and the precise screen layout have not been verified in this fixture.
