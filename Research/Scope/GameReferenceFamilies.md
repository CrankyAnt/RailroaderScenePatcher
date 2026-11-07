# Game Reference Families: Track Spans and Scene References

## TrackSpan storage and ownership

`Track.TrackSpan` is a `UnityEngine.MonoBehaviour`, not a DTO or plain value object. Its private `_lower` and `_upper` fields are marked `[SerializeField, HideInInspector]` and contain `Track.SerializableLocation` values (segment ID, distance, and end). `OnEnable` registers each instance with `Track.Graph`; `UpdateCachedPointsIfNeeded` resolves the endpoints and emits `Span {name} {id} missing points or invalid` or `Error finding points of {spanId} from {a} to {b}` when resolution/routing fails.

There are two observed creation/storage paths:

- Native editable map data: `Model.Ops.Data.TrackSpanDTO` stores lower/upper location strings. `Model.Ops.IndustryDataLoader.CreateIndustrySpans` creates runtime `TrackSpan` components with `GameObject.AddComponent<TrackSpan>()` and resolves OpsDTO span definitions. These are runtime projections of JSON, not a separate scene repair target.
- Unity scene hierarchy: `Track.Signals.CTCBlock.get_Spans` calls `GetComponentsInChildren<TrackSpan>()`. Therefore a CTC block can consume serialized `TrackSpan` MonoBehaviours placed in its scene hierarchy, whose endpoint fields are Unity-serialized. A missing/invalid endpoint can produce the same TrackSpan diagnostic. This is a concrete scene-backed reference path, but no affected Bryson block or mod reproduction was confirmed.

`CTCAutoSignal.OnEnable` separately logs `Signal {…} has null block` when its block list contains a null. That is a scene component relationship, not a graph location; JSON endpoint edits cannot restore a missing CTC block object. The presence of this warning does not prove it is triggered by rail deletion.

## Progression

The inspected progression types do not contain a TrackSpan or serialized track-location field. `TrackActivationMilestone.trackGroups` and `TrackConstructionMilestone.constructTrackGroups` are serialized group-ID lists; construction queries graph segments by group. `MapFeature` likewise targets track groups, map objects, areas, and ops entities. Removing or renaming a referenced group may affect progression, but I found no emitted missing-track-reference diagnostic or demonstrated rework failure for these fields. Treat them as group-ID integrity candidates, not a confirmed second runtime-patch family.

## Scope conclusion

For a shared runtime patcher beyond `TrackMarker`, the only concrete additional family found here is scene-authored `TrackSpan` endpoint repair when a legacy CTC hierarchy owns those span components. It is technically distinct from native OpsDTO spans, but remains a candidate pending an actual warning/reproduction. Keep JSON OpsDTO span repair in diagnostics or map-data tooling unless the project explicitly expands to JSON mutation; it does not by itself establish a need for a shared runtime DLL. No game, mod, or save files were changed.
