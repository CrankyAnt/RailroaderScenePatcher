<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene patch file v1

This reference defines the v1 JSON fields accepted by the core engine for fixed and track-following placement, activity, track-marker relationships, crossing coverage and starter placements. Each supplying mod keeps one scene patch file at `Data/scene-patches.json` in its own mod, as strict UTF-8; a leading byte-order mark is accepted. The runtime reads it and never writes it. The native map editor's Save writes it for the edited mod, as described in [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). Current native target support covers independent boards, described in [move a board with its terrain effects](moving-boards.md), the Ela bridge, described in [move or disable a bridge with its collision](placing-bridges.md), Generic track markers, described in [repair, replace or detach a track-marker binding](repairing-track-bindings.md), the detection of existing crossings, described in [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md) and [add detection for extra tracks at an existing crossing](adding-crossing-detection.md), and starter placement markers, described in [place a starter placement marker on the track](placing-starter-markers.md). The native observations and their limits are described under Current native targets below. The runtime capability classification remains `Unchecked`.

## Scene patch file fields

| Field | Type | Requirement |
| --- | --- | --- |
| `formatVersion` | integer | Required. Must be `1`. Read before anything else: a scene patch file whose version is missing, not the number `1` or newer applies none of its modifications and is protected from overwrite, whatever the rest contains. |
| `sourceMap` | object | Required. Must contain `mapId`. |
| `sourceMap.mapId` | string | Required and non-empty. It must identify the selected map definition or one of its resolved map layers. |
| `modifications` | object | Required. An object keyed by modification ID. Each key is non-empty and not only whitespace, and each value is an object. |

A property name that repeats in any object, at any depth, rejects the scene patch file during parsing. A repeated modification key, circuit ID or addition ID is such a name, so duplicate IDs reject the scene patch file.

The engine applies a scene patch file only when its `sourceMap.mapId` matches the selected map definition identifier or a resolved map layer identifier. A mismatch rejects the scene patch file before target resolution or mutation.

A scene patch file that scene patch editing creates names the bottom map layer of the edited map definition: the lowest of all map layers the game composes for it, the layers of its map layers included. It names the edited map definition itself when that has no map layers. For a map definition layered on `rr/bushnell-whittier`, the scene patch file names `rr/bushnell-whittier`. It then applies on a save of the base-game map, where the mod's map-changing script brings its native changes and the mod's own map definition is not loaded, and on every map layered on it, the mod's own map included. Scene patch editing never changes the target map of an existing scene patch file, also not on Save. To apply a scene patch file only on your own map, set `sourceMap.mapId` to your map definition's identifier by hand. **Copy ID** copies the target map of the edited mod's scene patch file, or, while that scene patch file does not exist, the target map scene patch editing would write for it. While the scene patch file cannot be read, **Copy ID** is unavailable and its tooltip gives the reason.

## Modification fields

The ID of a modification is its key in `modifications`. A modification has no `id` member. A modification that carries one fails as unsupported content, with a diagnosis that names its key and says that the ID is the key; the engine preserves it and still applies the other modifications.

| Field | Type | Requirement |
| --- | --- | --- |
| `target` | object | Required. Must contain `scene`, `path`, and `component`. Optional `structure` is a string. |
| `placement` | object | Optional. A fixed placement contains `mode`, `position` and `rotation`; a track-following placement contains `mode`, `track`, `offset` and `rotation`. See Placement below. |
| `active` | boolean | Optional. `false` disables the object, `true` enables it. Generic activity requires patcher 0.2.0 or later; track-marker activity requires 0.5.0 or later. |
| `relationships` | object | Optional. Explicit relationship settings; see Relationships below. Requires patcher 0.5.0 or later. |
| `starterPlacement` | object | Optional. The track location of a starter placement marker; see Starter placement below. Requires patcher 0.5.0 or later. |
| `dependsOn` | array of strings | Required. Each value names another modification in the same scene patch file. |

The target object accepts only `scene`, `path`, `component`, and optional `structure`. All required target strings must be non-empty. The scene adapter must resolve the identity to exactly one object. Zero or multiple matches fail the modification.

The order of the keys in `modifications` has no meaning, and neither has the order of the members of any object or of the values of `dependsOn`. The engine applies each modification after every modification its `dependsOn` names. Modifications that `dependsOn` does not order are applied in the ordinal order of their keys, defined under Canonical order below, so a scene patch file applies the same way whatever the order of its text. A value that `dependsOn` repeats counts once. A modification whose dependency fails is not applied; modifications that do not depend on it still apply. In the native map editor, an edit that would take a modification out of the scene patch file while another modification names it in `dependsOn` is refused and names those modifications, and an edit of a modification that does not take effect because a dependency is missing, fails or forms a cycle is refused with that reason.

A modification holds the desired state of one object and must contain `placement`, `active`, `relationships`, `starterPlacement`, or a combination. An omitted member leaves that part of the object untouched by this mod. An explicit `active` value is applied on every application, so a later activation by the game does not survive the next application of a scene patch file that disables the object. Patcher 0.1.0 rejects a modification that contains `active`; the dependency minimum below prevents that combination.

The [scene patch editing inventory](inspect-scene-objects.md) supplies observed identifiers and placement without changing this format. An inspection-only entry does not enable a new v1 operation. The engine rechecks the resolved object and map context immediately before mutation. A missing, ambiguous or replaced target fails rather than redirecting the operation. Scene patch editing integrations can bind a complete scene patch file edit to its inspected observations through an in-memory token; that token is not serialized or distributed.

[World selection](select-world-targets.md) confirms scene-object identities for the existing `target` fields. Segment and node choices record their native IDs and loaded map context in the scene patch editing session. Version 1 stores fixed and track-following placement, activity, track-marker relationships and crossing coverage. A pending choice or confirmed track or node identity stays in the scene patch editing session. Applying an edit and using the native map editor's Save writes the mod values.

## Placement

`mode` selects the placement. Any other `mode` value makes the modification unsupported; the engine preserves and does not execute it. The adapter defines the meaning of optional `structure`. Placement is separate from logical binding: placing an object never changes its track relationships, and a relationship never moves the object. A track-following placement keeps its track reference inside `placement`, never in `relationships`, so changing one leaves the other unchanged.

A fixed placement accepts only `mode`, `position`, and `rotation`. `mode` is `fixed`. `position` must contain three finite JSON numbers in map coordinates. `rotation` must contain four finite JSON numbers, representing a nonzero quaternion. The board and bridge adapters require a unit quaternion and perform the game map/world coordinate conversion.

A track-following placement accepts only these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `mode` | string | `trackFollowing`. |
| `track` | object | Required. A segment location with exactly `segment`, `end` and `distance`, with the same rules as a `trackMarker` reference, or an endpoint with exactly `node`, a non-empty track-node ID. |
| `offset` | array | Required. Three finite numbers in metres, in the track's frame. |
| `rotation` | array | Required. Four finite numbers, a nonzero quaternion applied after the track's rotation. |

The engine reads the track's native pose in map coordinates when it applies the modification. For a segment location this is the track graph's position and rotation at `distance` metres from the named end. The frame's forward axis (+z) points along the segment away from that end: for `end` `a` towards end `b`, for `end` `b` towards end `a`. For an endpoint the frame is the node's own native position and rotation. The object's position is the track position plus `offset` turned by the track rotation; its rotation is the track rotation followed by `rotation`. Applying the same scene patch file again yields the same placement; offsets do not accumulate.

A segment or node that is missing, or a distance outside the segment, fails the modification before mutation, and the object keeps its placement. The engine never substitutes another track. Every application evaluates the track's current pose, so a world load after the track moved places the object at its new position relative to the moved track. In the native map editor only the edited mod's scene patch file applies, and the patcher also moves its following objects while their track moves; see [follow a track or endpoint](following-track.md). In ordinary play, every applicable scene patch file evaluates the track's current pose at world load. The stored `offset` and `rotation` do not change when the track moves.

## Relationships

The `relationships` object accepts `trackMarker`, `crossingCoverage` and `crossingEquipment`, and must contain at least one of them. Each relationship has three settings:

| Setting | Meaning |
| --- | --- |
| Omitted | The relationship stays as it is. This mod does not touch it. |
| A reference | Sets or replaces the relationship. |
| `{ "detach": true }` | Clears the relationship. |

A `trackMarker` reference is an object with exactly these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `segment` | string | Required and non-empty. The ID of a track segment in the loaded track graph. |
| `end` | string | Required. `a` or `b`: the segment end the distance is measured from. |
| `distance` | number | Required. Finite, not negative, and at most the segment's length, in metres. |

A detach object contains only `detach` with the value `true`. `null`, `{}`, `false` and a detach mixed with reference fields are invalid. Unknown relationship kinds and unknown fields inside `trackMarker` make the modification unsupported; the engine preserves and does not execute it. The target must resolve to an object whose adapter supports the relationship. The patcher refuses a relationship on any other object instead of ignoring it.

The engine checks the reference before mutation. A segment that does not exist, a segment ID that matches more than one segment, or a distance outside the segment fails the modification, and the object keeps its relationship and registration. The engine never substitutes another target.

### Crossing coverage

`crossingCoverage` requires patcher 0.5.0 or later. It is an object that holds `circuits`, `additions`, or both:

| Field | Type | Requirement |
| --- | --- | --- |
| `circuits` | object | Optional. Keyed by circuit ID: the ID of the circuit's native island span, which must name a circuit of the target crossing. Holds one entry per explicitly set circuit, and at least one. |
| `additions` | object | Optional. Keyed by addition ID. Holds one entry per detection addition, described under Detection additions below, and at least one. |

An entry under `circuits` contains either the three spans or a detach, and no member that names the circuit:

| Field | Type | Requirement |
| --- | --- | --- |
| `approachA`, `island`, `approachB` | object | Required together, unless the entry detaches. Each holds `from` and `to`, both track locations with the `segment`, `end` and `distance` fields above. |
| `detach` | boolean | `true`, alone in the entry. The crossing stops evaluating the circuit. |

The three settings apply per circuit. An omitted circuit keeps its effective coverage, and an omitted `crossingCoverage` leaves every circuit untouched. Selected tracks replace the circuit's spans with spans the patcher owns, covering the route from `from` to `to`; `from` is the native lower end, which lies next to the island for each approach. A detached circuit leaves the crossing's evaluation. In both cases the circuit's native spans stay registered and unchanged, so a still-valid old route stops triggering the crossing without being removed. Circuits keep their identity and native order.

A `crossingCoverage`, `circuits` or `additions` value that is not an object, a circuit or addition key that is empty or only whitespace, and an empty `crossingCoverage`, `circuits` or `additions` object each fail the modification as an invalid definition. The engine preserves that modification and still applies the other modifications. A key that repeats inside `circuits` or `additions` is a repeated property name and rejects the whole scene patch file, as described under Scene patch file fields above.

The engine checks every entry before mutation: the circuit must exist on the crossing, each segment must exist exactly once, each distance must lie inside its segment and no span may have zero length. At least one circuit of the crossing must keep detection: a modification whose result, together with the crossing's current effective coverage, detaches every circuit is refused. A refusal leaves the crossing's coverage unchanged. Unknown fields in `crossingCoverage` or in an entry make the modification unsupported; an entry with `detach` and span fields, `"detach": false`, an entry that is not an object and a partial set of spans are invalid.

#### Detection additions

A detection addition makes the target crossing detect explicitly selected extra tracks. It is a detection addition: a circuit the patcher builds and owns, beside the crossing's native circuits. An entry under `additions` is keyed by the addition's ID, which is unique in the scene patch file and scoped to the supplying mod. The entry contains exactly these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `approachA`, `island`, `approachB` | object | All required. Each holds `from` and `to`, both track locations with the `segment`, `end` and `distance` fields above. |

The ID is the addition's persistent identity. The patcher joins it with the supplying mod's mod identifier, so two mods that choose the same ID get separate additions, and each application of a scene patch file finds the addition it built before. An ID may appear in one modification of a scene patch file. When two modifications use the same ID, both fail with that reason, whichever comes first in the file.

The three spans state the addition's track membership and detection area. The segments they name are the tracks that get detection, and the distances bound the area. The engine takes neither from track near the crossing. An entry under `additions` never contains `detach`. An addition exists while the scene patch file lists it. Removing the entry deletes the addition.

The engine checks an addition before mutation like selected tracks: each segment must exist exactly once, each distance must lie inside its segment and no span may have zero length. An addition counts as detection for the rule that a crossing keeps at least one detecting circuit. A scene patch file result that leaves no detecting circuit or addition is refused.

For each addition the engine builds one circuit with three owned spans, registers the spans in the track graph and adds the circuit to the controller after its native circuits. Added circuits follow each other in the order of their scoped identity, which within one mod is the ordinal order of their IDs, not the order of the scene patch file text. A repeated application reuses the circuit and every span whose tracks are unchanged, and replaces the spans of a role whose tracks changed. Deleting an addition removes its circuit from the controller, unregisters its spans and destroys them. Native circuits and other additions keep their state.

### Crossing equipment

`crossingEquipment` requires patcher 0.5.0 or later. It is an object keyed by equipment ID, with at least one entry: the lights and gates the supplying mod adds to the target crossing controller. Being listed under the controller's modification is the explicit controller relationship: the controller drives the addition. The key is the persistent ID of the addition: 1 to 64 letters, digits, `.`, `_` or `-`. The patcher scopes it to the supplying mod, so the same ID in another mod's scene patch file names a different addition. An ID may appear in one modification of a scene patch file; when two modifications use the same equipment ID, both fail with that reason. An entry holds no `id` member and contains exactly these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `pattern` | string | Required. The supported source pattern the source must conform to. Version 1 supports `crossing-light-gate-v1`. |
| `source` | object | Required. `scene` and `path`, both non-empty strings: the Unity scene name and hierarchy path of a device loaded in the map. |
| `placement` | object | Required. A fixed placement, as under Placement: `mode` `fixed`, `position` and `rotation` in map coordinates, for the root of the copy. |

An addition the scene patch file omits stays untouched; this applies to the additions of other mods. Within the edited mod's own scene patch file, an addition that the scene patch file no longer lists is removed when scene patch editing removes or edits it, and is simply not rebuilt on the next load. A repeated application keeps one copy per scoped ID, moves it to a changed placement, and rebuilds it when its `source` or `pattern` changes.

A `crossingEquipment` value that is not an object or is empty, an empty or invalid key, an entry that is not an object, a missing pattern, a source without a scene or path and a placement that is not fixed each fail the modification as an invalid definition. A key that repeats is a repeated property name and rejects the whole scene patch file. Unknown members in an entry, an `id` member included, unknown members in its `source` or `placement`, and a pattern this version does not know make the modification unsupported; it is preserved and not executed.

The engine checks every addition of a modification before any change: the pattern must be supported, the source must resolve to exactly one loaded object and conform to the pattern for the target controller (see [rejected sources](adding-crossing-equipment.md#rejected-sources)). A source that is not loaded, including one in a scene that is not loaded, and an ambiguous source fail the modification. The engine never loads a scene and never substitutes another source.

`crossing-light-gate-v1` requires that each lamp (`Track.KeyValueLampMaterialController`) and gate (`RollingStock.Controls.KeyValueBoolAnimator`) in the source observes the key the controller publishes as its lights or gates key, that lamp targets lie inside the source and use the lamp material, that each gate has an `Animator` and a clip, that the source holds at least one lamp or gate and no crossing controller, key-value object, track span, culler, bell player or missing script, that the source lies in the destination controller's Unity scene, and that the destination controller has exactly one key-value object and at most one culler.

An equipment modification is refused before any change while an addition of the target crossing fails its check. A modification that leaves the crossing's equipment as it is, such as a coverage change, neither checks nor touches the additions.

[Explicit snapping](snapping-to-track.md) writes a fixed placement. Scene patch editing computes a point on the explicitly selected segment's cubic curve or uses the selected node's position, preserving rotation, activity and logical binding. The graph selection and geometry checks belong to editing preflight and are not serialized. Runtime application and reload use the resulting absolute placement without querying a snap target. Snapping adds no dependency minimum; later track movement does not change the saved placement. Track-following placement is the separate mode above.

## Starter placement

`starterPlacement` sets the track location of a starter placement marker: the place on the track where a new-game car placement puts the front of its car cut. The target names the marker with `component` `Track.TrackMarker` and `structure` `starter-placement-v1`. The member accepts only these fields:

| Field | Type | Requirement |
| --- | --- | --- |
| `setup` | string | Required and non-empty. The identifier of the setup whose car placement reads the marker. |
| `track` | object | Required. A track location with exactly `segment`, `end` and `distance`, with the same rules as a `trackMarker` reference. `{ "detach": true }` is read and refused. |

The cut extends from the track location towards the end that `distance` counts from: for `end` `a` towards end `a`, for `end` `b` towards end `b`. Omitting `starterPlacement` leaves the marker's track location untouched by this mod. A starter placement marker accepts no `placement`, `active` or `relationships`: the object's placement and activity do not change where the cars start, and its track location belongs to `starterPlacement`.

Before mutation the engine refuses, as an invalid definition, a track location whose segment is missing or ambiguous, naming the segment, and one whose distance lies outside the segment, with the distance and the segment's length; no other segment is used instead. It refuses a detach, a `setup` other than the setup whose car placement reads the marker, and a marker that no car placement of an active setup reads, or that more than one reads, with the reason. It also refuses a location at which the whole car cut, together with the cuts of the setup's other placements, fails the game's placement rule, naming the car placement, its cars and length, and the game's reason. The other cuts stand at the track locations that the scene patch file's other starter placements set, and at their current locations otherwise, so the result does not depend on the order in which the modifications apply; see [place a starter placement marker on the track](placing-starter-markers.md#what-the-patcher-checks-first). A refused modification leaves the marker as it was, and the other modifications still apply.

## Several mods

During ordinary play the runtime considers each loaded mod that has `Data/scene-patches.json` and lists `crankyant/scene-patcher` under `dependencies.required` in its `Info.json`. It processes those scene patch files in Railroader's load order, the order of the game's loaded mod list. Map-layer order plays no part, and the scene patch file has no priority field. A scene patch file whose `sourceMap.mapId` matches neither the selected map definition nor one of its resolved map layers is skipped without a warning. The Player log has one line per scene patch file for each load attempt, such as `... map rr/bushnell-whittier (layers none): My mod [me/my-mod] scene patch file for rr/bushnell-whittier: 2 of 2 modifications applied, 0 failed, 0 unchanged`, or `... scene patch file for me/my-map skipped: not the loaded map or one of its layers`. In the native map editor only the edited mod's scene patch file is applied, as described above.

A failed scene patch file or modification does not undo the work of scene patch files processed before it and does not stop the scene patch files after it. An unreadable scene patch file or unreadable `Info.json` skips that mod as a whole.

When scene patch files of different mods each modify the same existing target, the runtime adds one informational game message for that object. It names the object as `scene/path [component]` and the mods with their identifiers in load order, and states any declared dependency among them. A further mod on the same object updates that message. The message reports a possible incompatibility, not a confirmed conflict: application continues in load order, no change is undone, and the later scene patch file's values are applied last. A declared dependency does not suppress the message. A target that resolves to no object or to several objects, or modifications from one mod alone, produce no message. The message is not a failure report and adds no JSON fields.

## Runtime failure reports

The runtime reports a failed scene patch file or modification in a structured game warning, separate from the historical file log. A modification warning identifies the source as Railroader Scene Patcher and includes the supplying mod name, identifier and version, `Data/scene-patches.json`, operation, target when resolved, error, patcher version and game version. The warning also includes the supplied author and an HTTP or HTTPS website when present. It does not invent contact details.

Invalid supplied definitions are reported to the mod author. Unknown failures and demonstrated patcher faults are reported to the patcher maintainer. The runtime reports a given source and operation once per loaded world. Invalid or unsupported scene patch file content that requires recovery remains a scene patch editing matter; this reporting behavior adds no JSON fields or format version.

## Current native targets

### Independent board

The native adapter recognizes independent boards. The target `scene` and `path` name the board object, its `component` is `Helpers.Culling.RendererCuller`, and `structure`, when present, must be `independent-board-v1`. The object must carry that culler and exactly one `Map.Runtime.MaskComponents.CircleMapMask`, both enabled, with active parents. The adapter refuses a mask that flattens terrain height and a hierarchy with a `Track.*` component, another culler, another mask component, another culling handler or a missing script. The board itself may be disabled through `active`. The adapter requires the map conversion, culling, mask, terrain tile and terrain completion functions it uses. A board placement has no track binding.

After setting the board placement, the adapter requests a culling-position update and rebuilds the circle map mask. It then verifies that the culling sphere and every mask registration are at the board's position, or, for a disabled board, that neither registration remains; a mismatch fails the modification and restores the captured placement, activity and registrations. The game regenerates terrain output at the old and new positions. The adapter reports each position complete after a tile-built notification covering it and idle terrain work with no pending invalidations. A position whose terrain tile is not built needs no rebuild. A position that has not completed after 30 seconds is reported with the completed and outstanding positions; the modification stays applied. Save does not write the generated terrain output; the game regenerates it from the saved placement. Native edited terrain tiles are separate and are not changed by a board modification.

After a failed mutation whose restoration verified, later modifications of the same board fail until the restored terrain output completes. If that output stops, or restoration cannot be verified, they fail for the rest of the load.

An isolated native test on Railroader 0.6.2 build 26335745 observed the rendered Sylva board, updated culling sphere and registered mask at the desired placement, with settled terrain output; its sampled tree regions were empty. A separate research trial on the same build moved the Sylva board's mask onto one generated tree, observed the tree leave the generated tile output and return after restoration. Other boards qualify through the component pattern without individual trials.

The bridge, track-marker, crossing-pad, crossing and starter placement targets below have their own adapters; other object families are not implemented.

### Bridge

The bridge adapter recognizes the Ela bridge root at `World/Large Scenery/Ela/Ela Bridge`, with target `component` `Helpers.Culling.RendererCuller`. Set `structure` to `bridge-colliders-v1`; the native map editor's Save writes it. Use the `scene` value from the inspected identity. A modification may set `placement`, `active`, or both, as for the board.

The adapter moves and enables or disables the bridge root. Its colliders and renderer cullers move with it; the adapter refreshes the culling position of every active culler in the hierarchy and synchronises the physics scene. The parents of the root and its root culler must be active, the hierarchy must contain at least one collider, no rigidbody and no track-owned component, and no construction-scenery entry may switch the root. Before mutation, the collider identities, shapes, enabled flags and positions relative to the root must equal their first observation in the load.

After a change the adapter verifies the root placement and activity, the collider layout, collider activity under the root, and that the loaded generated trestles within 250 m of the bridge and the construction-scenery controller that references the bridge are unchanged. Separate track-owned scenery is never part of the moved hierarchy. A difference fails the modification and restores the captured bridge state; an unverified restoration blocks further operations on the bridge for the load. See [move or disable a bridge with its collision](placing-bridges.md) for the scene patch editing route and limits.

### Baked scenery group

The group adapter recognizes the group roots of the Baked scenery group family. The target `scene` and `path` name the group root, its `component` is `UnityEngine.Transform`, and `structure`, when present, must be `baked-scenery-group-v1`; the native map editor's Save writes it. A group root is a scene object one level below a folder of `World`, and below `World/Large Scenery` one level below a town folder. A folder whose children are crossings, or crossings and road bridges, is not a group root; its children are. The family lists a group root that the scene serializes, that is no `Map.json` object, that no other family lists, and that holds a renderer, collider or light at or below it. A candidate without a renderer, collider or light is not listed at all. The family leaves out a group that holds, at any depth, a scene ground mask, a track marker, a station click spot, a car loader, a lake surface, a log pond, a storage pile, an industry storage display, bridge construction scenery, a spawn point, a map icon or a pickable toggle, and road bridges and rail bridge constructions outside Ela, including the parts below the `Almond Bridge` and `Plate Girder Bridge` folders; those, and the crossbucks of unprotected crossings, are inspection only. A path that names no listed group root, or more than one, fails the modification before mutation. A modification may set `placement`, `active`, or both.

The adapter sets the root's physical placement and its own activity; everything below the root moves with it, and the activity of the parts stays theirs. Placement is in map coordinates and converted through the current origin as for other objects below `World`. After a move the adapter refreshes the culling sphere of every enabled renderer and game-object culler in the group, moves the sphere of every simple culler, and synchronises the physics scene. A disabled group's renderer cullers release their spheres; a simple culler on the root stops switching the root while the group is disabled and resumes when it is enabled. The group must hold no missing script and no rigidbody.

After a change the adapter verifies the root placement and activity, the placement of every part relative to the root, the own activity of every part that no culler switches, and that every enabled culler's sphere stands at its culler. A difference fails the modification and restores the captured group; an unverified restoration blocks further operations on the group for the load. See [move or disable a baked scenery group](moving-baked-scenery-groups.md) for the scene patch editing route and limits.

### Track marker

The track-marker adapter recognizes an object holding one Generic `Track.TrackMarker`. The target `component` is `Track.TrackMarker`; set `structure` to `track-marker-v1`. The object must not be a milepost or a crossing pad; a Generic marker inside a crossing setup, such as a whistle sign, qualifies; no other component may listen to the marker's location changes; a `Track.TrackRelativePosition` whose activity follows a track group must activate this object itself, and no other component may have replaced its activation callback; no loaded component other than that relative position may hold a direct reference to the marker, and a scan that cannot read a field able to hold a marker refuses every marker; and no new-game car placement may use it. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination.

A `trackMarker` setting writes the marker's serialized location and its named-location registration in the track graph: the adapter removes the registration under the marker ID, writes the location, and registers the marker at the new location when it is enabled and active. A detach leaves no registration. Map labels below the marker are registered with the map again, so their dimming follows the new location's track group. Placement moves the object root and refreshes its renderer cullers; it never changes the binding. Before mutation the adapter requires one registration of this marker at its current location for an enabled, active marker with a resolvable location, and none otherwise. After a change it verifies the location, the placement, the activity, that registration rule and the map labels' track group. A difference fails the modification and restores the captured location, registration, placement and activity; an unverified restoration blocks further operations on the marker for the load. See [repair, replace or detach a track-marker binding](repairing-track-bindings.md) for the scene patch editing route and limits.

#### Markers with group-driven activity

A marker object whose `Track.TrackRelativePosition` has its activity follow the track group subscribes to the track graph's group event for the group of its bound segment, and the game activates or deactivates the object with that group. For such an object:

- A `trackMarker` setting also moves that subscription: after the location change the adapter runs the relative position's own unsubscribe and observe steps, so exactly one subscription observes the new segment's group. A detach, or a location that does not resolve, leaves no subscription.
- `active: false` is an explicit disable. The adapter replaces the relative position's activation callback with a hold that keeps the object inactive, before anything else changes, so a later group activation cannot activate it, not even briefly, including during a repeated application.
- An omitted `active` leaves the object's activity as it is. Without an explicit disable the game's group activates and deactivates the object. An explicit disable from another modification of the same object stays in place, whether that modification is in the same scene patch file or in another mod's scene patch file.
- `active: true` is refused before mutation, because the object's activity belongs to its track group. Omit `active` to keep group-driven activity.
- Removing the disable in the native map editor, by clearing `active` or removing the modification, releases the hold and observes the group again, so the object's activity follows the group's current state.

After a change the adapter also verifies the subscription count, the observed group and the activity the group or the hold requires. A difference fails the modification and restores the captured location, registration, subscription, hold, placement and activity; an unverified restoration blocks further operations on the marker for the load.

#### Markers with a duplicate marker id

The game removes a marker's graph registration by marker id from every segment, so it removes the registration of every scene marker that holds that marker id. When the target holds a duplicate marker id, a modification that would change the target's activity, its `trackMarker` binding or set a `trackFollowing` placement fails before mutation. The holders are the loaded scene markers with the target's marker id and every scene marker that owns a registration under it, loaded or not. This applies to Generic markers and crossing pads. The failure is an invalid supplied definition; its diagnosis names the duplicate marker id and every holder, with each one's activity. The scene patch file's other modifications still apply, including a fixed `placement` of the same marker, unless a map label below the marker caches another track group than its location, which only registering the marker again would correct. A value equal to the marker's current one changes nothing and applies, such as `"active": false` on an inactive marker or the location the marker already has. A registration that another holder owns does not count as a registration of the target, so it does not fail the registration rule before mutation.

### Crossing pad

The crossing-pad adapter recognizes an object holding one `Track.TrackMarker` of type `Crossing` or `Generic` and one `Track.TrackCurveMeshBuilder` with a mesh container and a mesh profile. The target `component` is `Track.TrackMarker`; set `structure` to `crossing-pad-v1`. A pad target named with `track-marker-v1` does not resolve, and a Generic-marker target never resolves to a pad. The consumer rules of the track-marker adapter apply, except that the pad builder may hold the marker, a pad inside a crossing qualifies, and a pad whose activity follows a track group is refused. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination.

A `trackMarker` reference on a pad moves the marker's location and graph registration as for a Generic marker, then runs the game's pad generation. Generation rebuilds the pad mesh along the track at the new location and moves the culling sphere to the new bounds; the game's culling recomputes the mesh visibility on its next update. `{ "detach": true }` fails the modification before mutation, because a pad without a location has no geometry; disable the pad with `"active": false` to hide it. A placement change also regenerates the pad, so the mesh stays on the track while the object moves. For a pad with `placement.mode` `trackFollowing`, the adapter also records the track curves the pad was generated from, the segment curves within half the pad width on either side of the marker location, and regenerates the pad when they change, even if its placement does not; scene patch editing checks this with its track-following reconciliation, including after native Undo and Redo of track edits, and regenerates once the changed curves have held still between two checks. A pad without track-following placement is not regenerated after a track edit by itself; every application of a pad modification, and its removal, regenerates the pad when the recorded source curves differ from the current track. After a change the adapter verifies the track-marker rules, that the current mesh was generated from the mod's location and the pad's current position, and that the culling sphere encloses that mesh. A pad generated by the game before the patcher observed it is generated once more on the first application, to the same mesh; later applications leave current geometry alone. A difference fails the modification and restores the captured location, registration, placement and activity, generates the pad again and requires the regenerated mesh to match the captured one; an unverified restoration blocks further operations on the pad for the load. One native journey on Railroader 0.6.2 with patcher commit `a282015` observed the pad Mg74 shift along the track after a rebinding, refusal of a missing segment and an out-of-range distance, native Undo and Redo, removal, and Save with a native map editor reload that reproduced one modification and one registration. A second journey with commit `ab2816f` measured that the rebinding changed the pad's vertex hash and moved its culling sphere 0.993 metres along the track while the sphere stayed aligned with the new bounds, and that native Undo and Remove modification restored the original vertex hash, bounds and culling sphere exactly. A pad that holds a duplicate marker id follows the rule under Markers with a duplicate marker id. See [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md) for the scene patch editing route and limits.

### Milepost

The milepost adapter recognizes an object holding one `Helpers.MilePost`, the Generic `Track.TrackMarker` it requires and its `Helpers.TextSynchronizer`. The target `component` is `Helpers.MilePost`; set `structure` to `milepost-v1`. A milepost target named with `Track.TrackMarker` or `track-marker-v1` does not resolve to the milepost, and a Generic-marker target on a milepost is refused before mutation. The consumer rules of the track-marker adapter apply, except that the milepost itself may listen to its marker's location changes and hold its marker. The object must hold no missing script and no `Track.TrackCurveMeshBuilder`. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination; each is a separate setting.

A `trackMarker` setting writes the milepost marker's serialized location and its graph registration as for a Generic marker, including the track-group subscription of a milepost whose activity follows its track group, and a detach leaves no registration. Placement moves the milepost root and refreshes its renderer cullers; it never changes the binding. `active: false` is an explicit disable; for a milepost whose activity follows its track group, `active: true` is refused before mutation, as under Markers with group-driven activity.

No setting changes the milepost's model, mile value (`mileage`), prefix, origin flag or displayed text, and no setting recalculates a mile value. The game keeps an origin cache that maps a prefix to the origin milepost object the first time something asks a milepost for its origin. The cache holds the object, not its location, so a placement or binding edit leaves the cache entry as it is: present with the same object, or absent. After a change the adapter verifies the track-marker rules and that the prefix, origin flag, mile value, displayed text, model and origin cache entry equal those before the change; the rendered texts count only when the milepost was active before the change, because a text that has never been awake fills in when the milepost first becomes active. A difference fails the modification and restores the captured location, registration, subscription, placement and activity; when the restored milepost state still differs from the captured state, the patcher reports incomplete cleanup and blocks further operations on the milepost for the load. The capability is incompatible with a game build whose milepost does more than call an empty `UpdateMileage` when its marker's location changes. A milepost binding is not repaired before the game's marker check at world load, so a broken milepost binding still raises the game's warning once in that load; the patcher repairs it at the normal application. A milepost that holds a duplicate marker id follows the rule under Markers with a duplicate marker id. See [reposition or rebind a milepost](repositioning-mileposts.md) for the scene patch editing route and limits.

### Crossing

The crossing-coverage adapter recognizes an object holding one `Track.Crossings.GradeCrossingController` whose circuit list is non-empty and whose circuits each hold an approach A, island and approach B span with an ID. The target `component` is `Track.Crossings.GradeCrossingController`; set `structure` to `crossing-coverage-v1`. A modification may set only `relationships.crossingCoverage` and `relationships.crossingEquipment`; `placement`, `active` and `trackMarker` are refused before mutation.

The adapter records the crossing's circuits and their native spans at its first observation in the load, which requires every native span to be registered in the track graph under its ID. A selected circuit gets three spans owned by the patcher, created disabled under a root that is not saved, given their ID and endpoints and enabled so that the native span lifecycle registers them; a repeated application reuses matching owned spans. The circuit then refers to them. A detached circuit is removed from the controller's circuit list and keeps its native references; the controller evaluates the remaining circuits in their native order from its next pass. A detection addition gets a new circuit set with three owned spans, built the same way, and the controller's list ends with the added sets. The adapter remembers each set it adds by the set's own identity, so an adapter created later over a world that still holds the set reconciles it and does not build a second one. Owned spans that no circuit uses are disabled, which removes their registration, and destroyed.

Before mutation the adapter requires that the controller evaluates only its native circuits in native order followed by the circuits it added, that each native circuit holds its native spans or owned spans, and that every native span stays registered. After a change it verifies each circuit's coverage, each addition's tracks, that no other added circuit remains, those rules, and that exactly the owned spans in use are registered and cover track. A difference fails the modification and restores the captured coverage, which removes a partly built addition and returns the captured additions; owned spans that stay registered after restoration make cleanup unverified and block further operations on the crossing for the load. The native map editor's Save writes neither the controller's references nor the added circuits nor the owned spans, so each load rebuilds them from the scene patch file. See [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md) and [add detection for extra tracks at an existing crossing](adding-crossing-detection.md) for the scene patch editing route and limits.

For each equipment addition the adapter creates a disabled wrapper with `HideFlags.DontSave` beneath the controller, records the scope, ID, pattern and source on it, instantiates the source beneath it at the placement, and checks that the copy's lamps and gates are its own components, that its gates keep their clip and that the controller's key-value object is the copy's nearest one. It then adds the copy to the controller's `Helpers.Culling.GameObjectCuller` list with the culler's current state and enables the wrapper; the copy's lamps and gates initialize through the game's restore notifier and observe the controller's keys. Removal takes the copy out of the culler list, disables and destroys the wrapper. At its first observation of a controller in a load the adapter adopts wrappers an earlier application left beneath it, so repeated world-load callbacks and retained worlds reconcile instead of duplicating; a crossing that no longer stands under its identity releases its additions with its owned spans. A construction failure removes the partial copy and restores the captured equipment; a copy that cannot be released makes cleanup unverified. See [add lights and gates to an existing crossing](adding-crossing-equipment.md) for the scene patch editing route and limits.

### Starter placement marker

The starter placement adapter recognizes an object holding one Generic `Track.TrackMarker` that a car placement of an active new-game setup (`Game.Progression.SetupDescriptor`) holds. The target `component` is `Track.TrackMarker`; set `structure` to `starter-placement-v1`. The object must not be a milepost or a crossing pad, the marker must have an ID, no other component may listen to its location changes, its activity must not follow a track group, and no loaded component other than a colocated `Track.TrackRelativePosition` may hold a direct reference to it. Such a marker never resolves as `track-marker-v1`. A modification may set only `starterPlacement`.

A `starterPlacement` track location writes the marker's serialized location and its registration in the track graph as for a Generic marker: the adapter removes the registration under the marker ID, writes the location, and registers the marker at the new location when the marker is enabled and active. A disabled marker keeps no registration; the game reads its location at a new company game all the same. The object's placement and activity stay as they are. Before mutation the adapter requires the registration rule of the track-marker adapter and refuses a marker whose marker id another scene marker holds, as the track-marker adapter does. After a change it verifies the location and that rule. A difference fails the modification and restores the captured location and registration; an unverified restoration blocks further operations on the marker for the load.

The game reads the location in `Game.State.CompanyModeSetup.Setup`, which runs when a new company game becomes active, after the world-load callback in which the patcher applies its scene patch files. The whole-cut check walks the track with the graph's own `LocationByMoving`, reads the segments' availability, and takes each cut's length from `StateManager.DescriptorsForIdentifiers` and `ConsistLength.OfDescriptors`, as the game's `TrainController.HandlePlaceTrain` does before `CanPlaceAt`. See [place a starter placement marker on the track](placing-starter-markers.md) for the scene patch editing route and limits.

## Writing by the native map editor

The native map editor's Save writes the scene patch file as indented UTF-8 JSON with line-feed line endings and no byte-order mark, in the canonical order below. It writes one modification per edited object and gives a new modification the key `place-<object name>`, `bind-<object name>` when its first edit is a track-marker relationship, `cover-<object name>` when it is crossing coverage, `equip-<object name>` when it is crossing equipment, `starter-<object name>` when it is a starter placement, or `follow-<object name>` when its first edit is track following, made unique with a numeric suffix. A prefix that the object name already starts with is written once, so the marker `starter-ewh-int2` gets the key `starter-ewh-int2`. Whitespace and comments of the source are not kept.

Reading a scene patch file never reorders it. Only a Save that writes the scene patch file produces the canonical order, and a Save writes it only when the scene patch file has an outstanding change.

The scene patch file is Saved exactly when its current content and the text last read or written have equal content. Two texts have equal content when both parse and, at every depth, object members match without regard to their order; the `dependsOn` of a modification that is an array of strings matches as a list sorted in ordinal order, duplicates counted; every other array matches in order; strings, booleans and null match exactly; and numbers compare by value. Two numbers are equal when the values they denote, each converted to the nearest IEEE 754 double, are equal: `1`, `1.0` and `10E-1` are equal, and so are two literals that differ only beyond double precision. Whitespace and comments never matter. An edit that leaves equal content leaves the scene patch file Saved, and the following Save writes nothing and leaves the file's bytes unchanged. When no scene patch file existed at load, the patch is Saved while it has no modification.

A Save refuses to overwrite a file that changed outside scene patch editing: the file's current content must equal the text last read or written under the same equality. A reorder or other spacing made outside scene patch editing is not a change and does not block the Save. A changed value, a file that cannot be parsed, a file deleted since it was read and a file created after a load that found none are changes; the file is protected from overwrite until the native map editor is reloaded.

Unknown fields at the scene patch file root and unknown modifications are preserved without being executed. Every number the native map editor did not replace keeps its source text, so a value such as `0.10` or a number beyond double precision is written back unchanged, also inside a modification that scene patch editing changed. The engine rejects unknown fields inside a modification, target, placement or relationship when it tries to execute that modification; the native map editor preserves such a modification and does not edit or remove it. A scene patch file with a missing, malformed, or unsupported format version, a scene patch file whose `modifications` is an array or any other value that is not an object, a scene patch file with an empty modification key or a modification that is not an object, a scene patch file in which a property name repeats in any object, a scene patch file for another map, and a scene patch file whose numbers cannot be matched to their source text are protected: the native map editor does not write them. Format version 1 has no earlier released shape; the array form of `modifications` is not converted. Scene patch editing shows where a protected scene patch file is wrong and offers to read it again after an outside correction or to start fresh behind a backup; see [Recover a protected scene patch file](recover-scene-patch-files.md).

### Canonical order

A Save that writes the scene patch file writes its content in this order:

- Scene patch file root: `formatVersion`, `sourceMap`, `modifications`, then unknown members.
- `sourceMap`: `mapId`, then unknown members.
- The keys of `modifications`, `circuits`, `additions` and `crossingEquipment` are in ordinal order.
- A modification: `target`, `relationships`, `starterPlacement`, `placement`, `dependsOn`, `active`, then unknown members.
- `target`: `scene`, `path`, `component`, `structure`.
- `relationships`: `trackMarker`, `crossingCoverage`, `crossingEquipment`.
- `crossingCoverage`: `circuits`, `additions`.
- `starterPlacement`: `setup`, `track`.
- An entry under `circuits` or `additions`: `approachA`, `island`, `approachB`, `detach`.
- An entry under `crossingEquipment`: `pattern`, `source`, `placement`.
- `source`: `scene`, `path`.
- A span: `from`, `to`.
- A track location, including a `trackMarker` reference and a `starterPlacement` track: `segment`, `end`, `distance`. A track-following `track` that names an endpoint holds only `node`, and a detach object holds only `detach`.
- `placement`: `mode`, `position`, `track`, `offset`, `rotation`. A fixed placement uses `mode`, `position`, `rotation`; a track-following placement uses `mode`, `track`, `offset`, `rotation`.
- The values of a modification's `dependsOn` are in ordinal order, duplicates kept, when `dependsOn` is an array of strings; otherwise `dependsOn` keeps its source form, including the member order inside its items.
- Every other array keeps its source order.
- In every object, unknown members follow the known members, in ordinal name order. This holds at every depth, including inside unknown objects and inside the objects of unknown arrays.
- Every number that an edit does not replace keeps its source literal.

Ordinal order compares strings by their UTF-16 code units, one unit at a time. A string that is a prefix of a longer string sorts first. `B` sorts before `Z`, `Z` before `_`, and `_` before `a`. A character outside the Basic Multilingual Plane sorts by its surrogate code units, so it sorts before U+E000 to U+FFFF.

## Dependency minimum

The supplying mod's `Info.json` must list `crankyant/scene-patcher` under `dependencies.required`; the runtime skips the scene patch file of a mod that does not. The native map editor's Save adds or raises that entry.

| Scene patch file content | Minimum patcher version |
| --- | --- |
| Fixed placement only | `0.1.0` |
| Any modification with `active` | `0.2.0` |
| An independent-board target other than the legacy Sylva location sign | `0.5.0` |
| Any modification whose target `structure` is `bridge-colliders-v1` | `0.5.0` |
| Any modification with `relationships`, a `Track.TrackMarker` target component, or target `structure` `track-marker-v1` | `0.5.0` |
| Any modification with `active` whose target `structure` is `track-marker-v1` | `0.5.0` |
| Any modification whose `placement.mode` is `trackFollowing` | `0.5.0` |
| Any modification with `relationships.crossingCoverage`, including a detection addition, a `Track.Crossings.GradeCrossingController` target component, or target `structure` `crossing-coverage-v1` | `0.5.0` |
| Any modification with `relationships.crossingEquipment` | `0.5.0` |
| Any modification whose target `structure` is `crossing-pad-v1` | `0.5.0` |
| Any modification with a `Helpers.MilePost` target component, or whose target `structure` is `milepost-v1` | `0.5.0` |
| Any modification with `starterPlacement`, or whose target `structure` is `starter-placement-v1` | `0.5.0` |
| Any modification whose target `structure` is `baked-scenery-group-v1` | `0.5.0` |

Fixed placement is the base minimum; other content in the scene patch file can raise it according to the table. The minimum follows the scene patch file content, whether or not the target is loaded or its capability is available on this build. The legacy Sylva location sign does not raise the pattern-based board dependency minimum. Save writes `crankyant/scene-patcher >=<minimum>` without an upper bound. It leaves an entry unchanged when a constraint already requires that version or a later one, keeps other constraints of the entry, and does not lower a minimum. When an existing constraint excludes the required minimum, Save reports the conflict and does not write the entry. Runtime reporting adds no scene patch file, schema or capability requirement.

A target whose `structure` names a capability that is incompatible with the running game build fails with that reason; other modifications still apply. A target preflight or mutation failure is recorded for the current load and is not retried during that load. If mutation cleanup cannot be verified, later modifications that resolve to that target fail closed.

## Application on each peer

Format version 1 has no multiplayer fields. The host and every client read `Data/scene-patches.json` from their own installed copy of the mod and apply it to their own scene. `sourceMap.mapId`, target identity and capability prerequisites are checked on each peer. `placement.position` is in map coordinates and is converted through each peer's own world origin. A repeated world-load callback reconciles the stored placement without accumulating offsets and does not retry failed modifications. A later load of the same map, including reconnect and same-map re-entry, starts a new load attempt. A target that still resolves under its identity keeps its applied state and any unverified-cleanup guard. A target that the game rebuilt gets the modification as new, and the patcher removes only what it owned for the old object, such as the spans of a detection addition. Failed modifications are retried once per load attempt. A cleanup failure is reported on that peer only and retried at the next load. Modification keys and addition IDs are the persistent identities across load attempts. In single-player, loading the same save again in one process rebuilt every modded object and added object, and each modification was applied once. The native behavior on reconnect has not been checked.

## Minimal example

```json
{
  "formatVersion": 1,
  "sourceMap": { "mapId": "example-map-id" },
  "modifications": {
    "place-object": {
      "target": {
        "scene": "BushnellWhittier-MapManager",
        "path": "World/Signs/Loc: Sylva Int",
        "component": "Helpers.Culling.RendererCuller",
        "structure": "independent-board-v1"
      },
      "placement": {
        "mode": "fixed",
        "position": [12.0, 2.0, 3.0],
        "rotation": [0.0, 0.0, 0.0, 1.0]
      },
      "dependsOn": []
    }
  }
}
```

## Transform gizmo editing

A completed [transform gizmo gesture](using-transform-gizmos.md) writes the same fixed placement as numeric editing. Position remains in map coordinates and rotation uses quaternion order. Preview frames are temporary and do not appear in the scene patch file. The gesture creates no logical binding. On an object that follows a track, the gesture's result is stored as a new `offset` and `rotation` relative to the same track. Native Save writes the edited mod's canonical scene patch file and dependency metadata; Undo and Redo reverse completed gestures through native history.
