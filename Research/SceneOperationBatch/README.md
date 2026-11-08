<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Scene operation batch preparation

This research supports the planning map [Define the first usable Railroader Scene Patcher](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/3). It prepares five existing investigations together. No game session or runtime trial was performed. The five questions remain open and the map's decision index is unchanged.

| Investigation | Prepared evidence | Pending result |
| --- | --- | --- |
| [Track-relative placement and group precedence](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/24) | [Marker group report](MarkerGroups/Report.md) | Placement, original and destination group behavior, explicit-disable precedence, restoration and material automatic reapplication behavior. |
| [Milepost origin cache and dependent display restoration](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/25) | [Milepost report](Milepost/Report.md) | Populated cache behavior across a meaningful rebind, actual rendered display, repeat and restoration. |
| [Crossing-pad geometry after marker changes](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/26) | [Crossing-pad report](CrossingPad/README.md) | Generated geometry, native update timing, visible result and restoration after the guarded rebind. |
| [Bridge collision and separate scenery restoration](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/27) | [Geometry report](Geometry/Report.md) | Actual contact, visible alignment, culling and independent approach restoration. |
| [Terrain output after independent board movement](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/28) | [Geometry report](Geometry/Report.md) | A relevant native rebuild, actual output at both sites and restored output. |

## Shared preparation

The [prior continuation report](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuation.md) and [continuation assessment](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/3e5970722bb278cbbaee003300ada6ce916df702/Research/ApplicableOperationsContinuationPlan.md) are immutable starting evidence. The probes target the existing disposable `crankyant/scene-operations-map`. The installed game and map assembly hashes match that earlier build. The fixture map hash is `36A99A8EBB6E2198C28D3C6D4AA2C859CE70B1F85E0D4E14243FA6837C9029AE`; the [build manifest](BuildIdentity.json) also records the fixture manifests and current authored inputs. It does not identify every game scene asset.

Run the following from the repository root. Build intermediates and output stay outside the checkout.

```powershell
pwsh -NoProfile -File Research/SceneOperationBatch/Prepare-SceneOperationBatch.ps1
```

The default command verifies the two installed assembly hashes and fixture map identity, compiles all five projects with warnings as errors, and stages only authored DLLs and manifests at `C:\TMP\RailroaderMapResearch\SceneOperationBatchStaging`. Every staged pack has one DLL and one `Info.json`. It writes exact source, fixture and output hashes to that staging directory's `BuildIdentity.json`. No vendor DLL, game asset or full player log is copied. The build manifest retained here records the completed preparation build. Compilation does not establish native-loader compatibility or runtime safety.

The active isolated home remains neutral. Its `Mods`, `ModDev` and `Saves` directories and activation arrays were checked empty after staging. No persistent Steam settings or personal preferences were changed. The existing exclusive test-environment allocation covers this batch; no other session may use that home during the eventual trials.

## Combined user-present session

The issue contracts require the user in the owning desktop session for runtime probes. Read the canonical `C:\Users\pghoe\source\repos\RailroaderScenePatcher\.docs\TestEnvironment.md` before continuing. The tests are pending that session. They do not require another product-scope decision or permission to prepare the probes.

With the user present and Railroader stopped, run:

```powershell
pwsh -NoProfile -File Research/SceneOperationBatch/Prepare-SceneOperationBatch.ps1 -Install -UserPresent
```

This verifies the neutral isolated home and stopped game, then installs the five verified packs and the single fixture there. It leaves preferences unchanged. Explicitly enable the selected packs in the isolated profile. Use the canonical one-launch `-homedir` and `-logFile` arguments; do not change persistent Steam options. Confirm the process command line and fresh log identify RuntimeHome. DLL changes require a fresh process. The five panels are draggable and do no automatic mutation.

Work sequentially within the shared fixture. First test marker placement/groups, restore and release that capture. Then test mileposts, because both cases use T-46. Follow with the crossing pad, bridge and board. Focus and let the origin settle before each capture. Capture telemetry and visible observations at baseline, after change, after repeat and after restoration. Do not use one probe while another has a pending mutation. A guard failure or incomplete restoration requires discarding the disposable world without saving. It does not justify forcing a field or bypassing a check.

Keep delayed observations separate from immediate mutation logs. TMP properties do not prove rendered glyphs, a ray hit does not prove physical contact, a culling registration does not prove visibility, and a mask descriptor does not prove terrain output. Record the exact compiled DLL and fixture identities with each trial. No screenshots or audio observations exist for this preparation.

The manual marker trial does not yet implement automatic marker reapplication on editor and ordinary-save reload. Its outcome must first establish which state and subscriptions need restoration. That remaining phase stays within the existing marker investigation; no claim from the board's earlier automatic-load trial is transferred to it. The crossing-pad probe tests a valid rebind and explicit native generation, which is sufficient to probe that consumer path before any separate placement experiment.

After the trials, exit without saving, retain reviewed publishable observations, and return active folders and activation flags to the canonical neutral baseline. Keep the staged case packs available for continuation. The original CaseLibrary packs remain untouched.

## Review and limits

Independent source review found and corrected incomplete baseline gating, stale-world cache restoration, incomplete graph registration comparison, partial event cleanup, missing camera/pause isolation checks and incomplete collider-set comparison. The staging script rejects unexpected pack contents and copies only the two verified files per pack. These are static corrections. They do not demonstrate runtime behavior.

The preparation adds no production patcher, JSON contract or release package. Static findings and clean builds do not meet the investigations' closure criteria. Each resolution still needs its own immutable runtime evidence and the named consumer decisions must remain blocked until that evidence is sufficient.
