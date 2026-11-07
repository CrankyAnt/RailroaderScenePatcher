# Why Sylva produces no TrackMarker warning during the load test

Checked against the installed original level4 scene and native core Map.json, the installed unmodified Katers Sylva Interchange 0.5.0 package, and its previously captured standalone load log. No game, mod or save files were changed.

## Package mechanism

The four-file native pack contains no DLL. Data.rl only calls map.merge(json.load("Data/Map.json")). No marker mutation, scene-object mutation or marker IDs appear in the package. Definitions.json registers a map. This package provides no evidence of an alternative native mechanism to rebind existing scene TrackMarkers.

The overlay adds 157 segment IDs, deletes 19 existing segments and changes flags on seven existing segments. It adds 152 node IDs, deletes six original nodes, changes the position or rotation of 30 original nodes and only the switch stand on three others. Missing keys inherit under partial merge; do not count absent keys as deletions or omitted endpoint fields as endpoint changes.

## Scene marker cross-check

The installed level4 scene contains 361 components referencing the Track.TrackMarker MonoScript. The script pointer was independently resolved against globalgamemanagers.assets: file 1, path 817, class TrackMarker, namespace Track, assembly Railroader. Marker ID, type, segment, distance and end were decoded from their serialized components. Native GameObject metadata was read separately.

| Marker | Original scene object | Effect of Sylva overlay | Interpretation |
| --- | --- | --- | --- |
| Mpg1 | Speed Sign Sylva Wye | Shp7 is explicitly deleted. Marker location remains Shp7, end A, distance 2.13723755 m. | Its original GameObject has m_IsActive=false while the component itself is enabled. The scene therefore does not initially activate this marker. This explains the absence of its warning at startup without a rebind. Later activation was not tested. |
| Mjd8 | T-45.1 Origin | Sozu remains with the same endpoint IDs; the mod changes its flags and moves/rotates both endpoint nodes N86n and Nam9. | Marker serialization is unchanged. Existing segment identity avoids a missing-segment error. No visual or functional alignment was verified. |
| M5pp | Whistle Sign E | Shuo remains unchanged as a segment record; its endpoint nodes Nmzo and N51l move. | Marker serialization is unchanged. Existing segment identity avoids a missing-segment error. No visual or functional alignment was verified. |

Mpg1 is the only scene TrackMarker found referencing one of the 19 deleted base segment IDs. The other 18 deleted segment IDs had no TrackMarker in this scene. Including surviving segments affected by changed endpoint nodes, only Mpg1, Mjd8 and M5pp were found among the 361 marker components. Sylva-area crossing markers Mcgx, Mmaz and M8dx reference Sg03, Sr40 and Stvw respectively; those segment records and their endpoint geometry remain unchanged by this overlay.

## Scope implication

The original assumption that no marker warnings might mean every marker-bearing track was preserved is too strong. One marker loses its segment but is initially inactive. Others retain their IDs while geometry changes. An absent warning does not prove correct physical positioning or that later activation is safe.

No alternative marker patching mechanism was found in this mod. The result does not invalidate the Ela and East Whittier runtime repair cases. It does show that warning-only discovery can miss inactive markers and valid-ID alignment problems. A future diagnostic inventory should list such references separately from demonstrated active runtime failures; it should not automatically rebind or activate them.
