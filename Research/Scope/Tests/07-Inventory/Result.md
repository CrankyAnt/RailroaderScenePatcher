# Inventory batch 1

Eight deliberate test cases were written to the existing `crankyant/inventory` map layer. The first Sozu deletion was made in the editor; remaining graph changes were made directly in that mod's native map JSON after closing the editor. The batch was then opened once with Edit Map. No normal save or other mod package was changed, and no repair DLL was loaded or created for these tests.

Primary evidence: `BatchManifest.json`, `Batch.Map.json`, the installed scene identities in `../../SceneBatchTargets.json`, and the new load section in `BatchReload.log`. The log confirms 1,866 nodes and 1,897 segments, matching two added nodes, five deleted base segments and one added segment. Four existing node positions changed; adjacent segments sharing those nodes are listed in the manifest.

| Case | Observed result after batch reload | Remaining check |
| --- | --- | --- |
| I01 milepost Mjd8 | Explicit missing-marker warning on deleted Sozu. | Runtime repair, visual relocation and mileage behavior remain untested. |
| I02 whistle M5pp with surviving Shuo | No M5pp missing-marker warning in the captured load section. The node movement also produces a switch geometry failure at N8xf, because adjoining Serb no longer intersects Ssup correctly. | This movement trial is confounded by neighboring switch geometry. Do not use it as proof of a clean general relocation. Visual pose comparison remains pending. |
| I03 speed Mwp1 | Explicit missing-marker warning on deleted Sjdl. An additional Yard Limit marker Mkwd on that segment also warns. | Yard Limit is an additional concrete marker use case. Its actual behavioral consumer is not established merely by its name. |
| I04 whistle Mm8w | Explicit missing-marker warning on deleted S9sc. An additional Station Sign marker Mi4f also warns. | Station Sign is an additional concrete marker use case. Do not assume its text or operational effect. |
| I05 crossing pad Mg74 | Explicit missing-marker warning on deleted Sj7c. | Native Wilmot station OpsDTO span also references Sj7c and fails to resolve. This is collateral native data, not evidence of a scene-only industry-span requirement. |
| I06 protected crossing | Crossing pad Mxwd explicitly warns on deleted Sz8t. Scene extraction establishes the crossing's Pipr/Pws9 span endpoint dependencies, but the captured load section does not name those spans. | A train/occupancy test is needed to establish controller behavior. No claim that gates, bells or circuits were tested. |
| I07 retained Ela bridge | Edited graph loads; no bridge-specific diagnostic identified. | Physical bridge alignment and collision remain unobserved. Absence of a diagnostic is not success. |
| I08 standalone Sylva board | Added track loads without a named board diagnostic. Its root has no TrackMarker in the inspected scene. | Visual obstruction at the board remains unobserved. A deliberate disconnected track island is part of this test. |

Seven distinct marker IDs warn: Mkwd, Mi4f, Mwp1, Mjd8, Mg74, Mm8w and Mxwd. Additional repeated CTC route failures name BC-TV 2 and WH-EL2, and some switches overlap block memberships. These are effects of intentionally breaking the graph and are not new object-repair promises. Repetition counts in DiagnosticSummary.json cover only the captured observation interval.

## Interpretation

The batch expands the concrete inventory with Yard Limit and Station Sign markers. It also provides an actual protected-crossing hierarchy with scene TrackSpan endpoints, but operational repair has not been demonstrated. Scene bridges and standalone boards remain visual candidates. This is a first inventory batch, not a completed compatibility or repair test suite. Signals remain outside V1.

Keep the intentionally broken inventory layer available for follow-up tests. The saved baseline retains the user's excavation circle. BuildBatch.ps1 records the construction, but is not an idempotent reset command; restore BeforeBatch.Map.json or the baseline deliberately before rebuilding.
