<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Board probe camera and origin audit

This bounded fact lookup supports the patch-coordinate decision in [Choose the patch document and target-identity contract](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/9). It checks whether the retained board probe directly forced the origin change observed after its camera jump. No game was launched.

## Canonical evidence

- [Exact Boot03 probe source](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/BoardContinuationProbe/Snapshots/359D2204A5493431B68D1751E6B554D509EC8F7A5197B3F0A5AB7C5232D21875.cs.txt)
- [Boot03 probe log](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/Evidence/Continuation/Boot03-BoardContinuationProbe.txt)
- [Native lifecycle and origin inspection](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableLifecycleStatic.md)
- [Continuation report](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuation.md)

## Exact probe behavior

The east-focus button calls `FocusFar`. That method checks the captured object, computes the captured game position plus 10,000 metres on X, and invokes `CameraSelector.JumpToPoint` with the strategy camera. It does not call a world-moving method or change the origin directly.

`WorldLoad` subscribes to `WorldTransformer.OnDidMove`. The subscribed `OriginMoved` method only writes a log line. Subscription does not invoke the event. The probe's uses of `WorldTransformer` otherwise read or convert coordinates.

The retained LF source reproduces its recorded SHA-256 `C55C81DC5FD10932729CD886019FC0CDB2466DC928AE8ED4A5C4C87246FB91EF`. Converting its line endings to CRLF reproduces the recorded original build-input SHA-256 `D5C5FFD0E700E70D611AEB08ED8FCFEA8298FA7764BE3AE6BB6FF170D919A19E`. Neither representation ends with a newline. This verifies source provenance against the report; it does not rebuild or independently verify the historical DLL.

## Native mechanism recorded by the original inspection

`WorldTransformer.CurrentTarget` derives tiles from `CameraSelector.shared.CurrentCameraPosition`. `MoveWorldIfNeeded` schedules a move when either absolute tile delta exceeds `tileRange`. `ScheduleMoveWorldDelayed` waits one second before setting the pending move consumed by `FixedUpdate`. `PerformMove` moves registered transforms and updates the offset, then invokes `OnDidMove` and publishes `WorldDidMoveEvent`.

The polling mechanism is not the complete camera-jump route. Read-only IL inspection on 2026-11-09 identified the immediate route below. The installed assemblies match the original research identities: `Railroader.dll` SHA-256 `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E` and `Core.dll` SHA-256 `ED9092B9A5918864FB35634544731303E53FE553C34F9FB84E98E2E514052EB9`.

## Camera-jump route verified in the installed assemblies

`CameraSelector.JumpToPoint` starts `_JumpToPoint`. The coroutine's `MoveNext` calls `StrategyCameraController.JumpTo`, then `CameraSelector.CameraJumped`. `CameraJumped` obtains the shared world transformer and calls `WorldTransformer.MoveNow` when it exists.

`MoveNow` cancels a pending delayed move and computes `CurrentTarget` from the current camera position. If that target differs from `currentTile`, it calls `PerformMove` immediately. This route does not use the polling path's `tileRange` threshold or its one-second delay. `PerformMove` updates the world offset and registered transforms, invokes `OnDidMove`, then publishes `WorldDidMoveEvent`.

The camera position and world origin are different values. However, this native camera-jump operation explicitly requests an immediate origin update. The probe does not attach its own additional origin-moving action to the button; its native `JumpToPoint` call reaches that behavior through the game code. Inspecting only the `JumpToPoint` wrapper would miss it.

## Observed trace and limits

The log records the east-focus command, an `OnDidMove` notification, and a later snapshot with a changed origin. The five-metre authored displacement remains in the object's game-coordinate position until the explicit restore command. Restore then deliberately removes that displacement and the test rotation using the captured game-coordinate pose converted under the current origin.

The east-focus log entry precedes the `OnDidMove` entry by about 18 milliseconds. The immediate native camera-jump route explains that short interval; attributing this observation to the one-second delayed polling route is unsupported. The trace does not contain a native call stack, so it does not independently record every intermediate call. The far point was outside the map and produced a zero-tile load diagnostic, which limits claims about normal in-map travel. The bounded result does not establish general restoration or production patcher safety.

The observed origin change and coordinate-aware restore remain evidence for the tested camera-jump scenario. They do not prove the trigger, timing, threshold, or behavior of origin management during ordinary train travel. A downstream contract must not promote the delayed-polling explanation to a finding about this camera jump.

The patch-coordinate decision uses game/map coordinates for a fixed authored location. It does not require replaying the probe's restore action or continuously resetting scene objects to their original game placement.

## Outside-map target and later west control

The frozen Boot03 source requests 10 km east. The later Boot04 snapshot and retained main probe source request 10 km west. The README records this difference. These sources establish the changed direction, not the reason for that change.

The [Boot04 log](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/Evidence/Continuation/Boot04-BoardContinuationProbe.txt) contains no `focus far` entry. It therefore does not establish an executed or successful 10 km west trial. Instead, both the ordinary-save and editor trials log a normal focus on the moved board at game position `(25697.9766,633.4856,441.813)`, followed by an origin notification with delta `(-21500,0,5000)`, a snapshot at origin `(25500,0,0)`, and restoration with `originChanged=True`. This is a separate recorded origin/restoration case that did not use the far-east camera target. It still uses a programmatic camera jump, not ordinary train travel.

The installed `_JumpToPoint` coroutine first requests priority loading around the resolved target, then selects and positions the camera, and calls `CameraJumped`. The inspected `CameraJumped`, `MoveNow` and `PerformMove` methods have no map-bounds or terrain-availability condition selecting a separate outside-map origin route. Strategy-camera `JumpTo` does use ground snapping, so missing terrain can affect other parts of the camera operation. Missing-ground behavior and all ordinary-navigation bounds were not established by this inspection.

The retained Boot03 game diagnostics log zero priority-load tiles at the east target. The return-to-board request names one tile and reports completion after about 0.773 seconds. Thus loading differed between these two requests. It is not valid to assume the blue-world case represents normal loaded-map rendering, terrain, or camera navigation. The evidence does not require the outside-map target to explain origin shifts, because the later board-focus trials also record them.
