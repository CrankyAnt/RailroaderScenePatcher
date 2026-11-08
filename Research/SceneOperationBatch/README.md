<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Scene operation batch evidence

This research supports the planning map [Define the first usable Railroader Scene Patcher](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/3). It prepares five existing investigations together. Preparation predates the runtime evidence linked below. The reports record each bounded result and its limits.

| Investigation | Evidence | Result and limits |
| --- | --- | --- |
| [Track-relative placement and group precedence](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/24) | [Preparation](MarkerGroups/Report.md), [Boot02](Evidence/Boot02/MarkerGroups/Report.md), [Boot03](Evidence/Boot03/MarkerGroups/Report.md), [Boot05](Evidence/Boot05/MarkerGroups/Report.md), [Boot06](Evidence/Boot06/MarkerGroups/Report.md) | Boot03 recorded temporary activation during repeat while explicitly disabled and a false native restore. Boot05 visibly demonstrated local placement and restoration. Boot06 applied locally after ordinary-save and editor loads; native restore succeeded but differed from the captured position before direct correction. Persistence remains untested. |
| [Milepost origin cache and dependent display restoration](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/25) | [Preparation](Milepost/Report.md), [Boot02 trial](Evidence/Boot02/Milepost/Report.md) | The trial observed a cache entry populated during its baseline, an unchanged rendered display, repeat and restoration. See the report for the boundaries of those observations. |
| [Crossing-pad geometry after marker changes](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/26) | [Preparation](CrossingPad/README.md), [Boot02 trial](Evidence/Boot02/CrossingPad/README.md), [Boot05 trial](Evidence/Boot05/CrossingPad/README.md) | Rebind alone left sampled geometry and culling unchanged; native generation changed them and visibly shifted the front pad. Generation after restoring the binding returned its visible alignment and matched sampled baseline signatures. The camera moved slightly after restoration Generate during verification. |
| [Bridge collision and separate scenery restoration](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/27) | [Preparation](Geometry/Report.md), [Boot02 trial](Evidence/Boot02/Bridge/Report.md), [Boot03 trestle diagnostic](Evidence/Boot03/Bridge/Report.md) | Boot02 recorded actual ball contact with the bridge collider at baseline, moved and restored phases. Immediate restoration matched captured local transforms and collider identities and shapes, with world poses within 0.01 m and 0.01 degrees. Boot03 sampled two nearby AutoTrestles unchanged through those phases; it did not test collision or identify the photographed timber. |
| [Terrain output after independent board movement](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/28) | [Preparation](Geometry/Report.md), [Boot02 trial](Evidence/Boot02/Terrain/Report.md), [Boot03 preflight](Evidence/Boot03/Terrain/Report.md), [Boot04 trial](Evidence/Boot04/Terrain/Report.md) | Boot03 found one generated tree inside the target core, then rejected movement before mutation because the guard expected a different radius. Boot04's native rebuild removed that tree from generated data and restoration returned the sampled baseline signature. Pixel-level disappearance at the target was not observed. |

## Shared preparation

The [prior continuation report](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuation.md) and [continuation assessment](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuationPlan.md) are immutable starting evidence. The probes target the existing disposable `crankyant/scene-operations-map`. The installed game and map assembly hashes match that earlier build. The fixture map hash is `36A99A8EBB6E2198C28D3C6D4AA2C859CE70B1F85E0D4E14243FA6837C9029AE`; the [build manifest](BuildIdentity.json) also records the fixture manifests and current authored inputs. It does not identify every game scene asset.

Run the following from the repository root. Build intermediates and output stay outside the checkout.

```powershell
pwsh -NoProfile -File Research/SceneOperationBatch/Prepare-SceneOperationBatch.ps1
```

The default command verifies the two installed assembly hashes and fixture map identity, compiles all five projects with warnings as errors, and stages only authored DLLs and manifests at `C:\TMP\RailroaderMapResearch\SceneOperationBatchStaging`. Every staged pack has one DLL and one `Info.json`. It writes exact source, fixture and output hashes to that staging directory's `BuildIdentity.json`. No vendor DLL, game asset or full player log is copied. [BuildIdentity.json](BuildIdentity.json) records the latest preparation build and may change when rebuilt. The frozen manifests for [Boot02](Evidence/Boot02/BuildIdentity.json), [Boot03](Evidence/Boot03/BuildIdentity.json), [Boot04](Evidence/Boot04/BuildIdentity.json), [Boot05](Evidence/Boot05/BuildIdentity.json) and [Boot06](Evidence/Boot06/BuildIdentity.json) identify their staged builds. Compilation does not establish native-loader compatibility or runtime safety.

After preparation staging, the isolated home was verified neutral: its `Mods`, `ModDev` and `Saves` directories and activation arrays were empty. Runtime evidence was later collected in that home. The [final neutral-state verification](Evidence/NeutralStateVerification.txt) records no game process, empty active `Mods`, `ModDev` and `Saves` folders, and preferences matching the neutral backup. No persistent Steam settings or personal preferences were changed. The exclusive test-environment allocation covers this batch; do not use that home concurrently with a trial.

## Runtime procedure

Before runtime work, verify the exclusive game lease, isolated test environment and fixture identity. Install while Railroader is stopped. Use:

```powershell
pwsh -NoProfile -File Research/SceneOperationBatch/Prepare-SceneOperationBatch.ps1 -Install -RuntimeAuthorized
```

The script verifies the isolated home and stopped game, then installs the five verified packs and the single fixture there. It leaves preferences unchanged. Explicitly enable only the selected packs in the isolated profile. Use the canonical one-launch `-homedir` and `-logFile` arguments; do not change persistent Steam options. Confirm the process command line and fresh log identify RuntimeHome. DLL changes require a fresh process. The panels are draggable. Mutation requires an explicit control, except that MarkerGroups can apply automatically after its auto-arm control is enabled and its readiness guard passes.

Work sequentially within the shared fixture. First test marker placement/groups, restore and release that capture. Then test mileposts, because both cases use T-46. Follow with the crossing pad, bridge and board. Focus and let the origin settle before each capture. Capture telemetry and visible observations at baseline, after change, after repeat and after restoration. Do not use one probe while another has a pending mutation. A guard failure or incomplete restoration requires discarding the disposable world without saving. It does not justify forcing a field or bypassing a check.

Keep delayed observations separate from immediate mutation logs. TMP properties do not prove rendered glyphs, a ray hit does not prove physical contact, a culling registration does not prove visibility, and a mask descriptor does not prove terrain output. Record the exact compiled DLL and fixture identities with each trial. No screenshots or audio observations exist for this preparation.

Boot02 did not test automatic marker reapplication on editor and ordinary-save reload. The manual marker observations do not establish which state and subscriptions reload requires. No claim from the board's earlier automatic-load trial transfers to this marker. The crossing-pad Boot02 probe tested a valid rebind and explicit native generation; it did not test placement.

Boot03's automatic marker case and nearby AutoTrestle diagnostic have runtime reports in the table. Its terrain target-movement preflight refused mutation and did not test output changes. Boot04 recorded generated-data removal and restoration after a relevant native rebuild; pixel-level disappearance of the target tree was not observed.

After each trial, exit without saving, retain reviewed observations, and return active folders and activation flags to the canonical neutral baseline. Boot02 evidence is retained under [Evidence/Boot02](Evidence/Boot02). Keep the staged case packs available for continuation. The original CaseLibrary packs remain untouched.

## Review and limits

Independent source review found and corrected incomplete baseline gating, stale-world cache restoration, incomplete graph registration comparison, partial event cleanup, missing camera/pause isolation checks and incomplete collider-set comparison. The staging script rejects unexpected pack contents and copies only the two verified files per pack. These are static corrections. They do not demonstrate runtime behavior.

Boot05 and Boot06 installed only MarkerGroups and CrossingPadProbe, alongside the same fixture. The other three packs remained staged. Boot05 added clear visual crossing and local marker observations. Its automatic editor preflight stopped without mutation when T-47 was not visible. Boot06 delayed its single focus request and recorded one local automatic application on each tested ordinary-save and editor load. Native restoration succeeded but required a separate captured-pose correction for exact baseline position. The clean ordinary save stayed byte-identical after the trial. These observations do not establish patch persistence or universal readiness.

The preparation adds no production patcher, JSON contract or release package. Static findings and clean builds are distinct from runtime evidence. Boot02 through Boot06 add bounded runtime observations, with limits stated in their reports. Boot01's exact DLL-to-source association remains unproven and exploratory; later frozen identities do not repair that evidence gap.
