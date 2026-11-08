<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Board continuation probe

This experimental probe targets issue 18 and the disposable `crankyant/scene-operations-map`. Manual editor and opt-in ordinary-world and editor load trials have run. See [applicable operations continuation](../ApplicableOperationsContinuation.md) for observations and limits. Do not use a personal save or save the disposable world after mutation.

## Static evidence

Mono.Cecil inspection of the installed Railroader 0.6.2 build 26335745 found `RendererCuller.RequestUpdateCullingPosition()`, which calls its token's `UpdatePosition(Transform, Nullable<float>)`. `RendererCuller.OnDisable()` disposes the token; `OnEnable()` calls `ResetCulling()`, which creates a new sphere. `MaskComponentBase.Rebuild()` calls `ApplyModifiers()`. The base implementation removes existing modifiers, and `MapMaskBase.ApplyModifiers()` creates modifiers using `MakeMaskDescriptor()`. `CircleMapMask.MakeMaskDescriptor()` reads the current transform position. `WorldTransformer.OnDidMove` is an instance event carrying a `Vector3`; `PerformMove` updates the origin and registered transforms before raising it. `WorldToGame` and `GameToWorld` convert positions using the current offset. These observations identify candidate calls, not proof that rendering or terrain output updates correctly. They are private implementation details subject to game updates.

The inspected `Railroader.dll` SHA-256 is `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`; `Map.Runtime.dll` is `9C52BD1FA8AA6D3D4417484F2002F985F6F4E01CBCD3C96E7675C4F19B8F0767`. Check both before a trial. Source and assembly identity are written to the probe log. Retain hashes of the exact built source and DLL with trial evidence.

## Build

From the repository root, with an external intermediate and output directory:

```powershell
dotnet build Research/BoardContinuationProbe/BoardContinuationProbe.csproj -p:GameManaged='D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='C:/TMP/RailroaderIssue18BoardBuild/obj/' -p:OutputPath='C:/TMP/RailroaderIssue18BoardBuild/out/'
```

## Trial and limits

Use the isolated RuntimeHome and only the scene-operations fixture and this probe. Focus, wait for the camera and origin to settle, then capture. Capture checks the exact loaded path `World/Signs/Loc: Sylva Int`, its three-node hierarchy, active state, parent, marker absence, culler and mask. It records the pose, node and renderer states, culler sphere and mask modifier keys. The focus control does not select the object. Open pause menu finds the single active loaded PauseMenu and calls SetPaused(true).

Move sets the captured game-coordinate position plus 5 m on X and rotates 15 degrees around world Y. Repeat uses the same baseline. Each move calls the culler update and mask rebuild before its snapshot. Wait several frames and snapshot again. Compare the sphere with the board position, modifier keys, visual appearance and game log. Modifier keys alone do not establish terrain output. Toggle explicitly disables or enables the board and records the state; observe both transitions. Restore reactivates the captured state, restores the captured game-coordinate pose under the current floating origin, calls both refresh methods and records a snapshot. Compare all affected state with the baseline. Restoration does not promise identical modifier keys or culling state because the game may create new registrations and update visibility asynchronously.

The probe logs world load, unload, origin notifications, commands and failures. On unload it cancels the capture and drops references without touching unloaded objects. Do not unload while dirty; restore first. If a step fails, inspect the probe and game logs and abandon the disposable session without saving. The editor trials observed origin shifts and restoration of the captured game-coordinate pose. This does not establish restoration safety in other conditions. The current far-focus control requests a camera jump 10 km west of the captured game position. The Boot03 snapshot instead requests 10 km east. It does not force an origin shift. Wait for the logged notification, snapshot, focus the board again and restore. Camera motion, rendering, collision, vegetation and terrain output require separate observation.

## Continuation instrumentation and opt-in load trial

The manual editor trial used the source frozen under `Snapshots/359D2204A5493431B68D1751E6B554D509EC8F7A5197B3F0A5AB7C5232D21875.cs.txt`. The ordinary-world and editor load trials used [this source snapshot](Snapshots/2A38EE98117E9F4DB48D33CFA1FF491157F1EF8D26FD61F5DFA6745A787A4D41.cs.txt), with original CRLF build-source SHA-256 `FBB78B33DF0C8F21A9DE9B4300CA1095B0168A08E64EBDDDE60FE97A1C738072` and DLL SHA-256 `2A38EE98117E9F4DB48D33CFA1FF491157F1EF8D26FD61F5DFA6745A787A4D41`. The retained LF source hashes are recorded in the evidence report. This continuation reads the current mask storage and logs the current and preceding keys, registration membership, descriptor bounds, radius and falloff, map offset, and pending invalidation count. Some component keys can refer to another modifier storage; absence from mask storage alone does not prove removal from every storage. A registered descriptor or an invalidation count does not establish rendered terrain output. World center is logged as descriptor game-space bounds center plus the map manager offset.

The file `ARM_AUTO_BOARD` in the probe directory opts in to a single automatic attempt per world load. It is absent by default. After origin subscription, the probe requires the first `-homedir` argument to match `C:/TMP/RailroaderMapResearch/RuntimeHome` and the probe context directory to match that home's `Mods/BoardContinuationProbe` directory. It logs the argument count without logging argument values. Later `-homedir` arguments do not prevent the attempt. The probe then uses the normal capture preflight, including the exact map and component checks, before calling the normal move. It logs the outcome. No delay or retry occurs. Remove the arm file after the disposable trial. Unload cancels the capture without restoring it. These trials do not establish general automatic application safety.
