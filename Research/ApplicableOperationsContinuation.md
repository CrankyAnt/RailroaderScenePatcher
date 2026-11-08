<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Applicable operations continuation

This record supports [applicable scene-object operations](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/18) and continues [the assessment](ApplicableOperationsContinuationPlan.md). It supplements [the earlier experiments](ApplicableSceneOperations.md) and [static lifecycle inspection](ApplicableLifecycleStatic.md). The observations cover disposable editor and ordinary-save routes on Railroader 0.6.2 experimental build 26335745. They use `crankyant/scene-operations-map`, sourced from `CaseLibrary/ModDev/SceneOperationsResearch`, and a separate broken-marker fixture. They do not establish a general runtime contract.

## Editor board trial

The scene-operations fixture's `Data/Map.json` SHA-256 was `36A99A8EBB6E2198C28D3C6D4AA2C859CE70B1F85E0D4E14243FA6837C9029AE`. The game assembly identities and inspected methods are recorded in the [board probe reference](BoardContinuationProbe/README.md#static-evidence).

The independently authored [frozen board probe](BoardContinuationProbe/Snapshots/359D2204A5493431B68D1751E6B554D509EC8F7A5197B3F0A5AB7C5232D21875.cs.txt) was built from CRLF source with SHA-256 `D5C5FFD0E700E70D611AEB08ED8FCFEA8298FA7764BE3AE6BB6FF170D919A19E`. The tested DLL identity was `359D2204A5493431B68D1751E6B554D509EC8F7A5197B3F0A5AB7C5232D21875`. The retained LF snapshot has the different hash recorded below. The DLL hash is the recorded build identity. The target was `World/Signs/Loc: Sylva Int`.

The operator observed the board move five metres and rotate 15 degrees, remain at the same pose when the operation was repeated, and return visibly to its original placement after restore. Disabling the board made it disappear and reenabling made it reappear. No screenshots were retained. The [probe log](Evidence/Continuation/Boot03-BoardContinuationProbe.txt) records the corresponding transforms, culling spheres, mask fields and activation state. The move and restore called `RequestUpdateCullingPosition` and `Rebuild`; the recorded sphere followed the board. The modifier keys changed between snapshots. This build did not read mask descriptors or establish terrain output or removal of prior modifiers.

After another move, the far-focus command requested a point 10 km east. The probe recorded `OnDidMove delta=-10000,0,0`, a snapshot at origin `(35500,0,0)`, then restoration of the captured game-coordinate pose with `originChanged=True`. At the far position the culler reported `NotVisible` and both renderers disabled, consistent with distance; this is not evidence of a failed restoration. Returning focus produced `delta=10000,0,0` and the operator observed the board visible at its restored placement. There is no after-return probe snapshot.

The [selected game diagnostics](Evidence/Continuation/Boot03-GameSelected.txt) record `RequestPriorityLoad: 0 tiles` at the far point outside the world, followed by the request and completion for the return tile. The session did not crash and return worked. This error limits the far-focus test; it is not evidence that normal in-world travel produces the same error.

## Automatic load and descriptor trial

A later opt-in probe build used the original CRLF build input represented by [frozen source](BoardContinuationProbe/Snapshots/2A38EE98117E9F4DB48D33CFA1FF491157F1EF8D26FD61F5DFA6745A787A4D41.cs.txt) SHA-256 `FBB78B33DF0C8F21A9DE9B4300CA1095B0168A08E64EBDDDE60FE97A1C738072` and DLL SHA-256 `2A38EE98117E9F4DB48D33CFA1FF491157F1EF8D26FD61F5DFA6745A787A4D41`, both verified locally. On the ordinary-save route, `OnWorldLoad` immediately captured and moved the board without a delay. After focus and an origin shift, a snapshot showed the moved pose. Repeat applied the same pose and restore recovered the baseline game-coordinate pose at the changed origin. The operator observed the board restored. The session used Quit and Discard; the clean backup's hash remained unchanged. This tests fresh application from a clean save, not persistence of a mutation in that save.

The probe inspected the mask storage. At baseline and after each rebuild, the current `CutTrees` modifier was registered with a curve descriptor. Its bounds center tracked the original and five-metre-shifted game positions; after the origin shift the logged world center also reflected the map offset. The immediately preceding key was absent from the inspected mask storage after move, repeat and restore. This does not establish absence from other storage. Pending invalidation counts were 326 during initial load, zero at the later snapshot, and one immediately after repeat and restore. Those counts do not establish that invalidation completed after restoration or that terrain and vegetation output changed correctly.

The same armed process rejected the default editor map with a wrong-map preflight error and remained clean. On loading the fixture, it captured and applied the move at `OnWorldLoad`. After focus and an origin shift, the later snapshot recorded the moved pose; restore recorded the original game-coordinate pose at the changed origin and the world unloaded with `dirty=False`. The operator observed the board move back toward its original position by the track; both renderers were enabled and the restored sphere center was `(192.976563, 633.4856, 441.8128)`. No screenshot was retained. The editor snapshot also recorded the old key absent from inspected mask storage on move and restore, and pending invalidations of zero before and one immediately after restore. This is one successful attempt per route under the probe's guard and is not a general readiness guarantee. Marker consumers and warning prevention were not exercised by the board mutation.


## Lifecycle trace

The [read-only lifecycle log](Evidence/Continuation/Boot03-MarkerLifecycleContinuationProbe.txt) records the default editor map followed by the fixture. At `MapWillLoadEvent`, the graph and matching markers were unavailable. On the fixture's `MapDidLoadEvent` and `OnWorldLoad`, the destination segment existed and both selected markers and their current and destination locations resolved. A later fixture load showed the same availability. This uses valid markers and a diagnostic counter beginning at mod load. It does not establish a pre-warning repair point or prove that all warning routes are covered.

A clean ordinary save was created through the native interface without a board mutation in this editor session, then Save & Quit was used. Its local backup `CleanBoard.railroad` has verified SHA-256 `B7806D37DFCC88759030FF35064B8F898BCDBFABFDDC918929F8CD0869A625B1`. It is a test input, not evidence of automatic reapplication or persistence. The automatic trial above used this save.

## Broken-marker warning trace

A read-only trace used the broken-marker fixture [map](BrokenMarkerProbe/Fixture/Data/Map.json), SHA-256 `697136472FA56EA4EB0F1FBB32D384B8352F744A4AB4A9A1023E076E69FEF944`. The lifecycle probe original CRLF build-source SHA-256 was `289A801D3824F99FEE4225DFBE122C1F8AA0759F3E3117F5BE6D565480A90C68` and its installed DLL SHA-256 was `8FB6A202F7FD608A114EC7DC73048A928254E0C7CA152BA0417DED5B31D1E969`. These identities were verified locally. No repair, diagnostic removal or fixture mutation was attempted.

On both the editor fixture load and a new ordinary sandbox load, `MapWillLoadEvent` reported no graph or matching markers. The application log callback then captured the M5pp invalid-track warning before `MapDidLoadEvent`. At both `MapDidLoadEvent` and `OnWorldLoad`, the target and replacement segment were available, the original location did not resolve, and the replacement did. The [probe trace](Evidence/Continuation/Boot05-MarkerLifecycleContinuationProbe.txt) records this ordering and the [selected native game diagnostics](Evidence/Continuation/Boot05-GameSelected.txt) corroborate the warnings. The operator also saw one M5pp warning in the native log window on each world. No screenshot was retained. The logged `warnedInvalid=False` field does not negate the observed warning. These two routes do not prove that every earlier callback lacks the required state or that warning prevention is impossible.

The new sandbox's native initial save was backed up as `CleanWarning.railroad`, verified SHA-256 `2A3F6982F1C33BEEB35DADB8B51ACE971D8CF896BDB9B322D3CC491F9FA4259E`. A fresh process loaded that existing save through native Start Game Play. The [fresh-process trace](Evidence/Continuation/Boot06-MarkerLifecycleContinuationProbe.txt) again captured the warning after `MapWillLoadEvent` and before `MapDidLoadEvent`. The target and replacement were available at the later callbacks, with the original location unresolved and the replacement resolved. The [selected native warning](Evidence/Continuation/Boot06-GameSelected.txt) corroborates the trace; the operator saw one warning in the native log window. No screenshot was retained. The save hash remained unchanged while loaded, and the read-only session exited without repair or diagnostic removal.

## Findings and remaining questions

The board probe refreshed its culling sphere and rebuilt the registered mask descriptor for the tested pose, including restoration across an observed origin shift. Automatic application succeeded once on each tested fixture route. Rendering of terrain and vegetation after invalidation, later invalidation completion, and general readiness remain unproven. The warning traces place the diagnostic before the two later callbacks in the tested broken-marker routes, without proving an earlier usable repair callback or excluding one.

The continuation assessment identifies other consumer-specific questions that these board and warning trials do not answer. They include track-relative placement and group subscriptions, the milepost cache and display, valid rerouted crossing membership and equipment, crossing-pad generated geometry, board terrain output, and bridge collision and separate scenery. Their state and restoration require separate evidence.

## Evidence provenance

The authored probe logs retain all lines with line endings normalized to LF. In each board log, native mask modifier GUIDs use stable aliases `m001`, `m002` and so on, ordered by first appearance within that log. Equality and previous-key relationships are preserved. The original local logs retain the GUIDs; their literal values are not needed to assess registration or removal. The Boot04 board log also omits one trailing space on each of original lines 213 and 215 to satisfy the whitespace check. Line ranges below include every line. Hashes are SHA-256 of the original bytes and retained bytes respectively. The original game log remains local because it contains player identifiers. Only the three diagnostic lines needed to document the far-point error and return request are retained from the Boot03 game log. Boot05 and Boot06 retain only the native warning lines listed below.

| Evidence | Original lines and SHA-256 | Retained SHA-256 |
| --- | --- | --- |
| [Board probe](Evidence/Continuation/Boot03-BoardContinuationProbe.txt) | 1-413, `98CFBC50E17FE1BC623DBA177042010B0BE2C34F4AD02278CB376F1AFFA8E0D6` | `67D84173FB253B816E5C34B9A0A737142F4460A23EB9D841B7CCDFE777C837F8` |
| [Lifecycle probe](Evidence/Continuation/Boot03-MarkerLifecycleContinuationProbe.txt) | 1-58, `9CF51EAF12B980C8AF478EBEDCFC128C65F37BA5926A88E72064FD02B6FFCCEB` | `E9EAF16F75DDD11AEFC2A635E330B9C71A0E16FF1179FCF0101977848A71AA34` |
| [Automatic board probe](Evidence/Continuation/Boot04-BoardContinuationProbe.txt) | 1-383, `3A74E5285F627EF461EC870B329ABC00BE6D3324B4822071D62572D146160685` | `9AC1FF84883AA35FEFE60F57EE1046080D8E754AAFE065C0FE10E9B658DFCBED` |
| [Automatic lifecycle probe](Evidence/Continuation/Boot04-MarkerLifecycleContinuationProbe.txt) | 1-54, `36627C73032DF5F9F5A646E56785B1F26E5BEF992857D0EDE07363A473702548` | `91843D63BC9C2603E19749A77A2FD77554247285AB9AD2B4F6E142551628E7E1` |
| [Fresh-process lifecycle probe](Evidence/Continuation/Boot06-MarkerLifecycleContinuationProbe.txt) | 1-22, `1130AC52F757909A9B900AC569923C2D6C6BE797ACBB093CF6CA4B6BBA167A67` | `600ED7EAA0B8F4AE9137497C8650FACDDF171FC599FB996F93731AD0ABFD4CDC` |
| [Fresh-process native warning](Evidence/Continuation/Boot06-GameSelected.txt) | PowerShell `Get-Content` line 72, `C16CDDE612B4F9CB46A8D606D50D12433AE39FBB94B16C55A7B30AD93F14EEDA` | `12082C27A78710EB64170B01928D41DBB5712E04133AFC68A4F51989450F541E` |
| [Warning lifecycle probe](Evidence/Continuation/Boot05-MarkerLifecycleContinuationProbe.txt) | 1-53, `F2D57DDBD58AC708BA106777C53A4FB200C0C4C735343CFB6E2AB9E711079B6B` | `C838B6B2AD575AFED18C69BA8080761F2AFFC0190F49D24AFA4E85E579CB2C8E` |
| [Warning game diagnostics](Evidence/Continuation/Boot05-GameSelected.txt) | PowerShell `Get-Content` lines 126 and 251, `5A2C436F0853088BBF4BC71DE65F210FC9A9ADA430F88B814D6119C4544F472B` | `9C1852447D5E6CB1E2FD83A745354FD6193ABEAF8048B2FC792418E4EFC1FBFD` |
| [Selected game diagnostics](Evidence/Continuation/Boot03-GameSelected.txt) | PowerShell `Get-Content` lines 238 and 240-241, `BFCE953B95D4C1AAE73E2BD6D01295CAF93214C668F8C17489032EE3EE25306C` | `69F711B95B32787D55D54427A5FB9AE9F2CDEC29A3BC5881C5D55292BD062FC8` |

The original probe files were `C:/TMP/RailroaderMapResearch/Observations/Issue18Continuation/Boot03/BoardContinuationProbe.log` and `MarkerLifecycleContinuationProbe.log`. The selected lines came from `C:/TMP/RailroaderMapResearch/Observations/Issue18Continuation/Player-Board03.log`. Log timestamps remain in their recorded format.

The automatic-trial original logs came from the respective probe directories under `C:/TMP/RailroaderMapResearch/RuntimeHome/Mods/`. They were copied after the fixture unloaded. The game log was not retained for this trial.

The warning trace original was `C:/TMP/RailroaderMapResearch/Observations/Issue18Continuation/Boot05/MarkerLifecycleContinuationProbe.log`. The selected warnings came from `C:/TMP/RailroaderMapResearch/Observations/Issue18Continuation/Player-Warning05.log`.

The fresh-process original probe log came from `C:/TMP/RailroaderMapResearch/RuntimeHome/Mods/MarkerLifecycleContinuationProbe/MarkerLifecycleContinuationProbe.log`. The selected warning came from `C:/TMP/RailroaderMapResearch/Observations/Issue18Continuation/Player-Warning06.log`.

## Source provenance

The build inputs used CRLF line endings. Repository files use LF. These SHA-256 values identify both forms; converting the retained source from LF to CRLF reproduces the recorded input bytes. The current board source is identical to the retained Boot04 snapshot. Source formatting does not imply a byte-identical rebuilt DLL.

| Authored source | Original build input SHA-256 | Retained LF SHA-256 |
| --- | --- | --- |
| Boot03 board snapshot | `D5C5FFD0E700E70D611AEB08ED8FCFEA8298FA7764BE3AE6BB6FF170D919A19E` | `C55C81DC5FD10932729CD886019FC0CDB2466DC928AE8ED4A5C4C87246FB91EF` |
| Boot04 board snapshot and current board source | `FBB78B33DF0C8F21A9DE9B4300CA1095B0168A08E64EBDDDE60FE97A1C738072` | `CD9628970C748FDBC621800C115F1802F976CCB2003E82429EF863C20F6EC4AA` |
| Lifecycle source | `289A801D3824F99FEE4225DFBE122C1F8AA0759F3E3117F5BE6D565480A90C68` | `1B3F9B81D362EF3C780BDBAAA9C3234528102452D831CD9A4F7F50BEB8A0FBE5` |
