# Katers Sylva standalone test

Game 0.6.2 (26335745), only kater/sylvainterchange 0.5.0 enabled. Fresh process loading existing MapModScopeSandbox, no editor. Data.rl modifyMap executed and map loaded successfully.

Three root configuration issues produced three ERR and two associated WRN entries:
- Diesel stand OKatSylInt_diesel-fueling-stand-2 sets params.supply to industry but has no sourceIndustryId. CarLoaderBuiltin reports that the structure will not dispense.
- IndustryDataLoader cannot resolve span P7ec at Sch6|a|42.938. It also reports the generated sylva-interchange.P7ec span as missing points or invalid.
- IndustryDataLoader cannot resolve span Pfaj at Sv6m|a|45.863. It also reports the generated sylva-interchange.Pfaj span as missing points or invalid.

The mod explicitly deletes Sch6 and Sv6m in graph.segments. Its map JSON contains no ops override for the inherited spans. The diesel supply error is directly visible in the mod's native object JSON. These are native map-data repair candidates, not evidence of scene-only CTC spans or a new requirement for the shared runtime patcher. No TrackMarker warning appeared at capture. No patches were applied during this test.

The base-game store_5_phys diagnostic remains present. Geographic and feature activation coverage is limited to this load.
