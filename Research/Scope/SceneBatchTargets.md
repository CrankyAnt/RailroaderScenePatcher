# Scene targets for isolated editor tests

These targets come from the installed Railroader 0.6.2 `Railroader_Data/level4` scene. `SceneBatchTargets.json` contains object and component path IDs, full hierarchy paths, serialized references and calculated world transforms. Unity path IDs identify objects in this installed scene, not stable public IDs across game updates. Positions and rotations describe the serialized scene before a floating-origin shift. No editor mutation, game behavior or runtime repair was tested here.

| Case | Scene target | Serialized location | Root path |
| --- | --- | --- | --- |
| Speed sign | `Mwp1`, marker component 29531 | `Sjdl`, distance 11.417938, end 1 | `World/Large Scenery/Alarka Signs/Speed Sign` |
| Whistle sign outside Sylva | `Mm8w`, marker component 31108 | `S9sc`, distance 58.324341, end 1 | `World/Large Scenery/Ela/Crossing Unprotected - Bryson Everett/Whistle Sign E` |
| Unprotected crossing pad | `Mg74`, marker component 30102 | `Sj7c`, distance 185.947662, end 0 | `World/Roads Wilmot/Crossing Unprotected/Crossing Pad` |
| Protected crossing | Controller 30941, GameObject 3431 | Two circuit sets below | `World/Large Scenery/Bryson/Grade Crossing - Bryson Everett` |
| Retained bridge candidate | GameObject 37, Transform 8588 | Scene position approximately `(8616.0015, 545.9800, 7274.7588)` | `World/Large Scenery/Ela/Ela Bridge` |

The three markers each have `Track.TrackMarker` and `Track.TrackRelativePosition`; the pad also has `Track.TrackCurveMeshBuilder`. Their root GameObjects and marker components are enabled in the scene. Active state of ancestors and runtime registration were not checked. The bridge root has `Helpers.Culling.RendererCuller`. Its nearby `Ela Bridge Scenery` root, GameObject 193, has `Game.Mow.TrackConstructionScenery`; do not assume moving either root moves the other. A nearby track segment and bridge collision ownership have not been identified, so confirm both in the editor before using this bridge as a destructive track test. `MapFeatures/Ela Bridge` and the similarly named progression objects are not the bridge visual root.

The crossing controller's serialized references point to six child `Track.TrackSpan` components. Mono.Cecil confirms the `CircuitSet` field order `approachA`, `island`, `approachB`. Both endpoints retain their serialized distance and end values in the JSON.

| Circuit | Approach A | Island | Approach B |
| --- | --- | --- | --- |
| Track 1 | `Pcyi`: `S8rj` to `Sx88` | `Pipr`: `Sz8t` to `Sz8t` | `Pws9`: `Sz8t` to `S9xx` |
| Track 2 | `Pdlh`: `S63c` to `S3ddf` | `P6id`: `Sccw` to `Sccw` | `P1db`: `Sccw` to `Snp1` |

UnityPy 1.25.4 read the scene and global script metadata. MonoBehaviour headers and fields were decoded from their serialized bytes because the scene does not include complete behaviour type trees. Controller references were resolved to scene component IDs and checked against the child names. Unity transforms were composed through their parent chains; quaternion values and raw precision appear in the JSON. This is static evidence only. Use separate disposable maps and record native editor availability, save/reload, graph registration and actual train/crossing behavior. Signals are outside this batch.
