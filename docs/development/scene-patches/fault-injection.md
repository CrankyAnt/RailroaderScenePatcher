<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Inject a failure into a native journey

A native journey cannot make a scene edit fail on demand with the normal patcher build. A test build of the patcher has hooks that a separate test mod arms: one mutation failure at a time, and beside it one top scene object that world marking reads as unreadable and one top scene object whose child objects the family reads as unreadable. With them, a journey in Railroader produces a failure after mutation, a failed restoration, an interrupted detection addition, a complex that world marking cannot read and a complex whose child objects the family cannot read, and compares the result with native before, after and restoration controls. Neither the hooks nor the switch exist in a normal build.

## Build the fault build and the switch

Build the patcher with `-p:SceneFaults=true`. The property defines `SCENE_PATCHER_FAULTS` in every project of that build through `Directory.Build.props`, which compiles `RailroaderScenePatcher.Core.FaultInjection` and its hooks. Build the switch mod `Tests/FaultInjection` beside it. Give the fault build its own build root, separate from a normal build:

```powershell
dotnet build Src/RailroaderScenePatcher --configuration Release -p:SceneFaults=true -p:GameManaged=<Railroader_Data/Managed> -p:BuildArtifactsRoot=<fault build root>
dotnet build Tests/FaultInjection --configuration Release -p:GameManaged=<Railroader_Data/Managed> -p:BuildArtifactsRoot=<fault build root>
```

The core tests run in both shapes. The normal run checks that the core assembly has no `FaultInjection` type. The run with `-p:SceneFaults=true` arms each point against controlled scenes:

```powershell
dotnet run --project Tests/RailroaderScenePatcher.Core.Tests --configuration Release -p:SceneFaults=true -p:BuildArtifactsRoot=<fault build root>
```

## Install and arm

1. Install the fault build of the patcher in the isolated test home, `C:\TMP\RailroaderMapResearch\RuntimeHome\Mods`, in place of the normal build. Install `SceneFaultSwitch.dll` and its `Info.json` from `<fault build root>\FaultInjection\bin\Release\net472` in a mod folder of its own there, and enable both mods in the test profile.
2. Start Railroader with the single `-homedir` of that test home. The switch shows a window titled **Scene Patcher fault switch (test only, F9 hides)**. In any other home it says it is inactive. With a normal patcher build it says that nothing can be armed.
3. Click one **Arm** button. **Armed** shows the point. The failure trips at the next mutation that reaches that point: an edit, an Undo or Redo step, or a world-load application. It then disarms, and **Last** shows what tripped and when, in UTC. **Disarm** cancels an armed failure that has not tripped.

| Button | What the next mutation does |
| --- | --- |
| Arm after-mutation | The mutation changes the object, then throws. The restoration runs normally, so the result is a restored failure. |
| Arm during-restoration | The mutation changes the object, then throws. The restoration throws before it writes anything, so the object keeps the failed attempt's state and the result is an incomplete cleanup. |
| Arm between-addition-steps | A crossing-coverage write that builds owned spans for a new detection addition throws after those spans registered and before their circuit set exists. Neither the write's own cleanup nor the restoration runs, as if the attempt stopped there. The result is an incomplete cleanup with the leftover spans. |

`between-addition-steps` waits for a write that builds a new addition. Other mutations pass it by.

The unreadable-group point is no mutation failure, and arming or tripping a mutation point leaves it armed. Type a top scene object in the text field in the form that the developer log line names it, the scene name and the path from the scene root, such as `BushnellWhittier-MapManager/World/Large Scenery/Bryson/Bryson Coal Dealer`, and click **Arm unreadable-group**. The top scene object is matched exactly as typed, spaces included. While the field is empty or holds only spaces, the button is unavailable and its tooltip reads **Unavailable: type a top scene object**. **Unreadable group** shows the top scene object. If arming fails, **Last** shows why until an arm succeeds or **Disarm** is clicked, and the Player log gets a `[SceneFaultSwitch] Arm unreadable-group failed:` warning. Every time world marking decides its complexes, it reads that complex as unreadable and shows its objects on their own, until **Disarm**. While Debug logging is on, the Player log gets one debug line for the top scene object and reason, `Scene Patcher <version> [debug]: The patcher couldn't finish preparing a group of scene objects at <top scene object>: its objects could not be read: Injected unreadable group`. World marking decides its complexes again for a new inventory snapshot and after each scene change, so arm the point before the world loads, or before **Select in world** first marks the complex.

The unreadable-parts point works the same way for the Scene object family and is armed beside the unreadable-group point. Type the top scene object in the same text field and click **Arm unreadable-parts**; the button is unavailable with the same tooltip while the field is empty. **Unreadable parts** shows the top scene object. Every time the family decides its complexes, it reads the child objects of that complex as unreadable: the complex keeps its top scene object entry without child objects, until **Disarm**. While Debug logging is on, the Player log gets one debug line for the top scene object and reason, `Scene Patcher <version> [debug]: The patcher couldn't finish preparing a group of scene objects at <top scene object>: its parts could not be read: Injected unreadable parts`. The family decides its complexes again after each change of the loaded `World` hierarchy, so arm the point before the world loads.

## Limits

The hooks sit in `PatchEngine` after `IScene.Apply` and before `IScene.Restore`, in `PatchEditingSession.Commit` after `IScene.Apply` and before its restoration write, and in the crossing-coverage write between span creation and circuit creation. They replace the failure, not the native work around it: the mutation and the native state it leaves are real. A failure inside a native adapter call itself, such as an exception raised by the game midway through a call, is not reproduced. Track-following reconciliation and **Recheck** have no hook of their own. A crossing-coverage write inside **Recheck** still reaches the `between-addition-steps` point, so an armed interruption can trip there: Recheck then reports an incomplete cleanup with the spans it built and restores nothing. Every attempt ends the injected failure it tripped, so a restoration that the failure required but that never ran cannot fail a later, unrelated restoration. The unreadable-group hook sits in `GroupSkips.Read`, before world marking reads the objects of one complex, and replaces only that read: the scene stays unchanged. The unreadable-parts hook sits in `GroupSkips.ReadParts`, before the family (`GroupWorld.Verdicts`) reads the child objects of one complex, and replaces only that read.

CI does not compile the fault build. It is checked locally only, with the fault core-test command above and the two fault builds.
