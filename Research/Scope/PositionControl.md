# Position control and Sylva location board

Read-only investigation on installed 0.6.2. No runtime movement test was performed and no game, mod or save files were modified.

## Confirmed code paths

TrackMarker.Location setter updates serialized _location, invalidates cached position and invokes OnLocationChanged. It does not set a Transform. OnEnable registers the location with Graph; changing an already enabled marker needs registration handling rather than assuming the setter reindexes it.

TrackRelativePosition stores _marker, targetTransform, targetOffset, targetAngle and snapToTerrain. TryApply obtains marker PositionRotation and MoveTransform converts game coordinates through WorldTransformer.GameToWorld, applies offset and angle, and writes target position/rotation. ConfigureAndApply and Apply provide explicit application paths. No TrackRelativePosition subscription to OnLocationChanged was found. Existing Ela repair changes Location before registration and verifies the graph; it does not call Apply and therefore does not prove visual relocation.

An illustrative scene signal MC-W Enter has CTCAutoSignal, TrackMarker, TrackRelativePosition and CTCSignalCuller on the same GameObject. CTCAutoSignal is not a TrackMarker subtype. Placement and signal aspect/block/interlocking behavior are separate. Moving its model and marker does not by itself redefine protected blocks, switches or routes.

## Exact Sylva screenshot subject

The original level4 scene contains World/Signs/Loc: Sylva Int. Its TextSynchronizer text is Sylva Interchange. Root GameObject 778, Transform 14311. Its only root scripts are RendererCuller, CircleMapMask and TextSynchronizer. Its children Sign-Location and Text hold mesh renderers and TextMeshPro. Neither the root nor children has a TrackMarker or TrackRelativePosition. Original local position under identity parents is approximately (25692.9766,633.4856,441.8128).

This is a standalone scene location sign, not one of the previously catalogued TrackMarkers. It can remain at its original location without any track warning even if nearby rails change completely. A runtime DLL can target and move its root Transform; this is an additional scene-object relocation operation rather than marker rebind. Its culling and map-mask behavior must be checked after a move; use game coordinates correctly under floating-origin world transforms. No native JSON mutator for this inherited object was demonstrated.

## Controlled tests before selecting scope

Keep prototype cases independent or independently togglable. Use a disposable native test map and a separate temporary runtime probe; leave original packs and normal save untouched. Record editor selection availability and native saved JSON, then compare runtime graph registration, visual pose and behavior.

Cases:
1. Delete the segment of an active whistle/crossing marker. Confirm warning. Rebind to replacement track and explicitly apply TrackRelativePosition; test a large relocation plus offset/rotation.
2. Move the Sylva standalone location-board root to an explicit destination. Confirm board, text, mask/culling and origin-shift behavior after reload. Verify no industry/interchange operational boundary changes are implied by moving its sign.
3. Rebind/reposition an existing signal as a distinct case. Inspect block spans, facing, switch/interlocking links and train detection/aspects; visual movement alone is not success.
4. Delete a scene-authored CTC span endpoint and test a guarded span repair. Distinguish it from native OpsDTO industry spans.

Do not mark any of these physical move cases as runtime-proven until the probe has been loaded and its result observed. Do not infer all object categories share a safe generic patch from Unity Transform access alone.
