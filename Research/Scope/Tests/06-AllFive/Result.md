# Combined map mod load test

Game 0.6.2 (26335745). All five native packs enabled, fresh process loading existing MapModScopeSandbox without opening the editor. The log confirms all five Data.rl modifyMap handlers executed and the map loaded successfully with 2084 nodes and 2118 segments.

After removing WRN/ERR timestamps and deduplicating repeated service errors, the combined diagnostic set exactly equals the union of the five individual diagnostic sets. No additional log error family or missing individual diagnostic was found at capture.

| Pack | Observed individual and combined result |
| --- | --- |
| Sox Ela 1.0.0 | Three invalid TrackMarkers and one terrain-snapped water-column anchor warning. |
| East Whittier 1.0.0 | Three invalid starter-ewh TrackMarkers. |
| Katers Sylva 0.5.0 | Two invalid inherited industry spans, plus diesel stand missing its industry supply source. |
| Whittier Crossover 1.0.0 | No WRN/ERR/Exception diagnostics. |
| Bryson Industries 1.0.0 | Three undefined load IDs, followed by initialization and service failures. |

Scope implication: TrackMarker repair is the only demonstrated scene-reference runtime repair family in this corpus. Native OpsDTO span endpoints, object anchors/supply and load definitions can be addressed in native mod data. Scene CTC spans and other relationships remain research candidates, not proven requirements. The results support a marker-focused first release, but do not establish complete editor coverage.

This is a startup diagnostic comparison, not proof that all five maps are physically compatible or every feature works. Distant tile loads, construction/progression, operations and silent geometry conflicts were not exercised. No map pack was patched in this round. The original normal save still matches its pre-test backup.
