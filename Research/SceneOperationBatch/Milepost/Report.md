<!--
SPDX-FileCopyrightText: 2026 CrankyAnt
SPDX-License-Identifier: MIT
-->

# Milepost cache and display trial

## Status and sources

The phased probe was prepared and compiled before runtime work. Boot02 executed it in the isolated test home. See the [Boot02 report](../Evidence/Boot02/Milepost/Report.md). That trial observed a cache entry that was absent at baseline, the dependent display, repeat and restoration. This run's scope did not include a preexisting cache entry, other prefixes or reload. [Milepost origin cache and dependent display restoration](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/25) defines the question. The [continuation assessment](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuationPlan.md) and [prior result](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableSceneOperations.md#milepost-root-experiment) identify the populated-cache case. The [prior source](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/MilepostProbe/MilepostProbe.cs) restores the cache after each snapshot, applies a 0.25 m change and completes all phases in one GUI action. Its result does not answer the Boot02 trial.

Private inspection used Mono.Cecil against the locally installed `Railroader.dll`, SHA-256 `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. The canonical local test environment identifies it as version 0.6.2 experimental build 26335745. The hash does not identify scene assets. No vendor method body or asset is included here.

Static inspection found that `Helpers.MilePost.Origin` looks up a prefix in a static dictionary and inserts the result of a scene search when absent. `MilePost.OnEnable` and `OnDisable` subscribe and unsubscribe its marker location callback. In this build the callback calls `UpdateMileage`, whose body has no operations. `TrackMarker.Location` updates serialized location, invalidates its cached position and invokes its location event; marker disable and enable remove and add named graph registration. `TextSynchronizer.ApplyText` assigns its field to child `TMPro.TMP_Text` components. These are static findings about this assembly, not evidence of rendered output or runtime ordering in the trial.

## Prepared probe

[MilepostProbe.cs](MilepostProbe.cs) and [Info.json](Info.json) define a native-loader DLL with a user-triggered panel. It requires the first separate `-homedir` argument to have the value `C:\TMP\RailroaderMapResearch\RuntimeHome` and its mod context directory must normalize to `C:\TMP\RailroaderMapResearch\RuntimeHome\Mods\MilepostProbe`. Later occurrences are ignored, matching the inspected native first-argument behavior; the log records only their count. These checks also apply before mutation and cache restoration. It selects the existing T-46 origin (`Mdow`, original segment `Stvw`) and dependent T-47 (`Mnau`, segment `Stky0cg9c`) only on `crankyant/scene-operations-map`. It rejects missing or duplicate identities, inactive markers, unexpected segments, absent graph registrations, unresolved locations and an unexpected T origin. It uses T-47's already resolved, separately registered location as the different-segment alternative. Boot02 used this fixture location. Its broader meaning and group association were not established.

The baseline reads the dependent origin to populate the T cache and keeps that entry through apply, observation, repeated assignment and restoration. Each mutation disables the origin marker, sets its location, and re-enables it. The panel holds each state until a separate capture action in a later frame. Captures record cache and resolved-origin identity, both graph registrations, location, root and child poses and activity, renderer and mesh identity, mileage, synchronizer and TMP text, and `TrackRelativePosition` group and observation state. These records are telemetry. Boot02 separately recorded the rendered dependent display at each phase. The probe checks restored telemetry against baseline, then reinstates the cache presence and identity that existed before the trial. An abort attempts to restore binding, registration and prior cache only while the captured map, graph, scene handles, objects and cache remain current. Before mutation and cache restoration the probe checks those identities. On world unload it drops the references without touching the binding or global cache. A world mismatch prevents further trial actions and requires abandoning the disposable session without saving. Boot02 did not exercise abort or world-mismatch handling.

The group checks require exactly one `TrackRelativePosition` on each selected post. If the installed fixture differs, the probe fails closed and the case must be revised. It does not toggle groups, prove group event subscriptions, inspect every model material, or establish all renderer internals. A matching text property does not prove a rendered glyph was visible. One prefix and one alternative do not establish general cache behavior.

## Build

From the repository root, the following command built the authored probe against the installed game references. The output is outside the repository.

```powershell
dotnet build Research/SceneOperationBatch/Milepost/MilepostProbe.csproj -p:GameManaged='D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed' -p:BaseIntermediateOutputPath='C:/TMP/MilepostIssue25/obj/' -o C:/TMP/MilepostIssue25/out
```

The preparation build succeeded with zero warnings and errors. The current batch [build manifest](../BuildIdentity.json) may change on rebuild. The frozen [Boot02 build identity](../Evidence/Boot02/BuildIdentity.json) records the staged pack and fixture identities for that trial. The build manifest does not establish loader compatibility or runtime behavior; the separate [Boot02 report](../Evidence/Boot02/Milepost/Report.md) records the runtime observations.

## Runtime procedure

Runtime work requires the exclusive game lease and canonical isolated test environment. Stop the game before installing the authored DLL and manifest. Use the disposable scene-operations fixture and enable only the selected case.

1. Obtain the exclusive game lease and follow the canonical `TestEnvironment.md`. Verify the DLL and fixture identities, use the isolated test home and the disposable scene-operations fixture, and enable only the chosen case. Install only the authored DLL and manifest into `RuntimeHome/Mods/MilepostProbe` with the game stopped. Start a fresh process and verify its test-home command line and log.
2. Select `Focus T-47`, position the camera so its actual display is legible and allow the floating origin to settle. Focus is available only before capture. Select `Capture baseline and populate cache`. Record a visual observation or screenshot and capture the probe log. If baseline refuses, stop and retain the error; do not force the trial.
3. Select `Apply alternative binding`. After the displayed state settles, inspect and record T-47's actual display, then select `Capture observed phase`. Select `Repeat binding`, inspect the display again, and capture that phase.
4. Select `Restore binding`, inspect the restored display, then capture the restored phase. Check the log for restored telemetry and prior-cache verification. Record any differences rather than inferring success from the button alone.
5. If any operation or restoration fails, abandon the disposable session without saving. Exit without saving in all cases, retain reviewed observations, and return the active test home and flags to the neutral baseline. Boot02 did not assess autosave or reload behavior.
