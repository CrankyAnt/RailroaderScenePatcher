<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Inject a failure into a native journey

A native journey cannot make a scene edit fail on demand with the normal patcher build. A test build of the patcher has hooks that a separate test mod arms, one failure at a time. With them, a journey in Railroader produces a failure after mutation, a failed restoration and an interrupted detection addition, and compares the result with native before, after and restoration controls. Neither the hooks nor the switch exist in a normal build.

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
3. Select one **Arm** button. **Armed** shows the point. The failure trips at the next mutation that reaches that point: an edit, an Undo or Redo step, or a world-load application. It then disarms, and **Last** shows what tripped and when, in UTC. **Disarm** cancels an armed failure that has not tripped.

| Button | What the next mutation does |
| --- | --- |
| Arm after-mutation | The mutation changes the object, then throws. The restoration runs normally, so the result is a restored failure. |
| Arm during-restoration | The mutation changes the object, then throws. The restoration throws before it writes anything, so the object keeps the failed attempt's state and the result is an incomplete cleanup. |
| Arm between-addition-steps | A crossing-coverage write that builds owned spans for a new detection addition throws after those spans registered and before their circuit set exists. Neither the write's own cleanup nor the restoration runs, as if the attempt stopped there. The result is an incomplete cleanup with the leftover spans. |

`between-addition-steps` waits for a write that builds a new addition. Other mutations pass it by.

## Limits

The hooks sit in `PatchEngine` after `IScene.Apply` and before `IScene.Restore`, in `PatchEditingSession.Commit` after `IScene.Apply` and before its restoration write, and in the crossing-coverage write between span creation and circuit creation. They replace the failure, not the native work around it: the mutation and the native state it leaves are real. A failure inside a native adapter call itself, such as an exception raised by the game midway through a call, is not reproduced. Track-following reconciliation and **Recheck** have no hook of their own. A crossing-coverage write inside **Recheck** still reaches the `between-addition-steps` point, so an armed interruption can trip there: Recheck then reports an incomplete cleanup with the spans it built and restores nothing. Every attempt ends the injected failure it tripped, so a restoration that the failure required but that never ran cannot fail a later, unrelated restoration.

CI does not compile the fault build. It is checked locally only, with the fault core-test command above and the two fault builds.
