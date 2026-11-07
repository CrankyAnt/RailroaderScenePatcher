# Scene-object and track-reference inventory

Research record for [issue #2](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/2), based on Railroader 0.6.2 experimental. This branch is supporting evidence and is never merged into the default branch. It is not a mod release or a supported test map.

## Findings

| Observation | Evidence | Limit |
| --- | --- | --- |
| Deleting referenced graph segments produced seven missing-marker warnings across yard limit, station sign, speed sign, milepost, crossing-pad and whistle markers. | [Batch result](Scope/Tests/07-Inventory/Result.md), [marker warnings](Scope/Tests/07-Inventory/MarkerWarnings.txt), [manifest](Scope/Tests/07-Inventory/BatchManifest.json) | The changes deliberately damage the map. A load warning does not establish safe repair or complete behavior. |
| Preserving a segment ID while moving its endpoints can retain a valid marker reference without moving the visible sign. | [Isolated whistle repeat](Scope/Tests/07-Inventory/IsolatedWhistleResult.md), [reload log](Scope/Tests/07-Inventory/IsolatedWhistleReload.log) | The original movement also distorted a neighboring switch. The isolated repeat removes that geometry confound but creates deliberate track gaps. |
| Some location boards have no TrackMarker or TrackRelativePosition component. | [Board component observation](Scope/SylvaBoardScene.json), [position analysis](Scope/PositionControl.md) | Their visual hierarchy, text, terrain mask and culling remain relevant to a future relocation experiment. |
| Moving track at the Ela bridge left the original scene bridge and piers in place; the generated track bridge followed the track. | [Final observation note](FinalObservations.md), [target inventory](Scope/SceneBatchTargets.md) | Visual mismatch and physical collision are separate questions. |
| The tested train passed the Ela bridge piers and the standalone Sylva board without a noticeable collision; it also traversed the intersecting test track. | [Final observation note](FinalObservations.md) | These are operator-reported driving observations for specific objects, not a general collision rule for all scenery. |
| Existing scene components and native map-data objects have different owning loaders. No generic native scene-marker override was established in the inspected loader paths. | [JSON loader audit](JsonLoaderAudit.md), [reference families](Scope/GameReferenceFamilies.md), [official capability review](Scope/OfficialCapabilities.md) | A bounded inspection cannot prove that no undocumented route exists. |
| JSON-only legacy map mods can depend on a separate runtime that reads the JSON. | [Legacy JSON context](LegacyJsonContext.md), [native rework examples](ReworkJsonExamples.md) | Legacy runtime formats do not establish experimental 0.6 support. |
| A native Data.rl map merge is needed to apply this disposable inventory map delta to the existing base-map sandbox save. | [Snapshot loader](InventoryModSnapshot/Scripts/Data.rl), [final observation note](FinalObservations.md) | Applying graph/map JSON does not implement scene-object patching. |

The evidence covers both objects with track-marker components and independent scene objects. It does not establish a working shared scene patcher, JSON schema, safe generic deactivation, marker registration refresh or compatibility range. Protected crossing circuits and scene TrackSpan behavior remain unproven operational candidates. Signal/interlocking behavior was not established by this inventory.

## Reading the archive

- [Official capabilities](Scope/OfficialCapabilities.md), [loader audit](JsonLoaderAudit.md) and [reference families](Scope/GameReferenceFamilies.md) describe the inspected documentation and code paths.
- [Candidate families](Scope/CandidateFamilies.md) and [editor matrix](Scope/EditorTestMatrix.md) are earlier proposed research designs, not completion reports.
- [Base-game control](Scope/Tests/00-BaseGame/), [individual map-mod tests](Scope/Tests/) and [combined test](Scope/Tests/06-AllFive/Result.md) retain their recorded conditions and version-specific diagnostics.
- [Inventory experiments](Scope/Tests/07-Inventory/) retain the baseline, small map deltas, experiment manifest and reload captures. A manifest entry marked pending belongs to the initial design; the later results and isolated-repeat report record what was actually observed.
- [Final observations](FinalObservations.md) add the later editor and driving results. Earlier notes saying collision was not yet tested predate these observations.
- [InventoryModSnapshot](InventoryModSnapshot/) preserves the locally authored disposable probe. It is evidence, not an installable or playable product.

These are dated research-step records. Statements about the repository not yet existing or actions not yet performed describe their original observation step, not the current repository state. Unverified leads remain unverified unless a later cited test resolves them.

## Primary-source boundaries

Official references include the [map-modding guide](https://docs.railroadergame.com/modding/map-modding/), [Data.rl documentation](https://docs.railroadergame.com/modding/data-rl/) and [migration guide](https://docs.railroadergame.com/modding/migrating/). The local code inspection concerns the installed experimental build; the archive does not distribute game assemblies, full Unity scenes, meshes, textures or downloaded third-party mod archives. Small JSON observation records contain identifiers, numeric positions, component relationships and test state; map delta files are the disposable experiment's authored changes.

## Publication treatment

[ArchiveManifest.json](ArchiveManifest.json) records original and published hashes for 64 source artifacts. Publication copies normalize line endings and remove local account paths, player identifiers and private Discord message references. The Discord note is an anonymous bounded-search summary. One unverified author-report attribution is qualified rather than presented as a confirmed statement. The experiment script accepts explicit input paths instead of a local account path.

Setup administration, publication audit deliberations and mixed planning/context notes are excluded from this branch. The research branch and issue conclusion preserve the findings; they do not define the product specification.
