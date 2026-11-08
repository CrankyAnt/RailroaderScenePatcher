<!--
SPDX-FileCopyrightText: 2026 CrankyAnt
SPDX-License-Identifier: MIT
-->

# Milepost cache and display trial

## Status and sources

The phased probe is prepared and compiles. It has not been loaded or executed in the game. The probe and its assumptions remain unverified. [Milepost origin cache and dependent display restoration](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/25) defines the question. The [continuation assessment](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuationPlan.md) and [prior result](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableSceneOperations.md#milepost-root-experiment) identify the untested populated cache and rendered display. The [prior source](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/MilepostProbe/MilepostProbe.cs) restores the cache after each snapshot, applies a 0.25 m change and completes all phases in one GUI action. Its result does not answer this trial.

Private inspection used Mono.Cecil against the locally installed `Railroader.dll`, SHA-256 `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. The canonical local test environment identifies it as version 0.6.2 experimental build 26335745. The hash does not identify scene assets. No vendor method body or asset is included here.

Static inspection found that `Helpers.MilePost.Origin` looks up a prefix in a static dictionary and inserts the result of a scene search when absent. `MilePost.OnEnable` and `OnDisable` subscribe and unsubscribe its marker location callback. In this build the callback calls `UpdateMileage`, whose body has no operations. `TrackMarker.Location` updates serialized location, invalidates its cached position and invokes its location event; marker disable and enable remove and add named graph registration. `TextSynchronizer.ApplyText` assigns its field to child `TMPro.TMP_Text` components. These are static findings about this assembly, not evidence of rendered output or runtime ordering in the trial.

## Prepared probe

[MilepostProbe.cs](MilepostProbe.cs) and [Info.json](Info.json) define a native-loader DLL with a user-triggered panel. It requires exactly one separate `-homedir` argument with value `C:\TMP\RailroaderMapResearch\RuntimeHome` and its mod context directory must normalize to `C:\TMP\RailroaderMapResearch\RuntimeHome\Mods\MilepostProbe`. These checks also apply before mutation and cache restoration. It selects the existing T-46 origin (`Mdow`, original segment `Stvw`) and dependent T-47 (`Mnau`, segment `Stky0cg9c`) only on `crankyant/scene-operations-map`. It rejects missing or duplicate identities, inactive markers, unexpected segments, absent graph registrations, unresolved locations and an unexpected T origin. It uses T-47's already resolved, separately registered location as the different-segment alternative. Runtime checks must confirm the fixture still has that location; its meaning and group association remain untested until execution.

The baseline reads the dependent origin to populate the T cache and keeps that entry through apply, observation, repeated assignment and restoration. Each mutation disables the origin marker, sets its location, and re-enables it. The panel holds each state until a separate capture action in a later frame. Captures record cache and resolved-origin identity, both graph registrations, location, root and child poses and activity, renderer and mesh identity, mileage, synchronizer and TMP text, and `TrackRelativePosition` group and observation state. These records are telemetry. The observer must inspect the rendered dependent display at each phase. The probe checks restored telemetry against baseline, then reinstates the cache presence and identity that existed before the trial. An abort attempts to restore binding, registration and prior cache only while the captured map, graph, scene handles, objects and cache remain current. Before mutation and cache restoration the probe checks those identities. On world unload it drops the references without touching the binding or global cache. A world mismatch prevents further trial actions and requires abandoning the disposable session without saving. These guards are untested at runtime.

The group checks require exactly one `TrackRelativePosition` on each selected post. If the installed fixture differs, the probe fails closed and the case must be revised. It does not toggle groups, prove group event subscriptions, inspect every model material, or establish all renderer internals. A matching text property does not prove a rendered glyph was visible. One prefix and one alternative do not establish general cache behavior.

## Build

From the repository root, the following command built the authored probe against the installed game references. The output is outside the repository.

```powershell
dotnet build Research/SceneOperationBatch/Milepost/MilepostProbe.csproj -p:GameManaged='D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='C:/TMP/MilepostIssue25/obj/' -o C:/TMP/MilepostIssue25/out
```

The build succeeded with zero warnings and errors. The final batch [build manifest](../BuildIdentity.json) records current LF source hashes and staged DLL and manifest hashes. The build does not establish loader compatibility or runtime behavior.

## Trial procedure, pending user presence

1. Obtain the exclusive game lease and follow the canonical `TestEnvironment.md`. Verify the DLL and fixture identities, use the isolated test home and the disposable scene-operations fixture, and enable only the chosen case. Install only the authored DLL and manifest into `RuntimeHome/Mods/MilepostProbe` with the game stopped. Start a fresh process and verify its test-home command line and log.
2. Position the camera so T-47's actual display is legible. Select `Capture baseline and populate cache`. Record a visual observation or screenshot and capture the probe log. If baseline refuses, stop and retain the error; do not force the trial.
3. Select `Apply alternative binding`. After the displayed state settles, inspect and record T-47's actual display, then select `Capture observed phase`. Select `Repeat binding`, inspect the display again, and capture that phase.
4. Select `Restore binding`, inspect the restored display, then capture the restored phase. Check the log for restored telemetry and prior-cache verification. Record any differences rather than inferring success from the button alone.
5. If any operation or restoration fails, abandon the disposable session without saving. Exit without saving in all cases, preserve only publishable observations, and return the active test home and flags to the neutral baseline. Autosave and reload behavior are outside this preparation.
