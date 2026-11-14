<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene-patch document v1

This reference defines the v1 JSON fields accepted by the core engine for fixed and track-following placement, activity, track-marker relationships and crossing coverage. Each supplying MapMod keeps one document at `Data/scene-patches.json` in its own pack, as strict UTF-8; a leading byte-order mark is accepted. The runtime reads it and never writes it. The map editor writes it for the active edited MapMod, as described in [edit and save fixed placement in the map editor](editing-placement-in-the-editor.md). Current native target support covers independent boards, described in [move a board with its terrain effects](moving-boards.md), the Ela bridge, described in [move or disable a bridge with its collision](placing-bridges.md), Generic track markers, described in [repair, replace or detach a track-marker binding](repairing-track-bindings.md), and the detection of existing crossings, described in [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md) and [add detection for extra tracks at an existing crossing](adding-crossing-detection.md). The native observations and their limits are described under Current native targets below. The runtime capability classification remains `Unchecked`.

## Document fields

| Field | Type | Requirement |
| --- | --- | --- |
| `formatVersion` | integer | Required. Must be `1`. |
| `sourceMap` | object | Required. Must contain `mapId`. |
| `sourceMap.mapId` | string | Required and non-empty. It must identify the selected Map definition or one of its resolved map layers. |
| `modifications` | array | Required. Each entry must have a unique string `id`. |

The engine applies a document only when its `sourceMap.mapId` matches the selected Map definition identifier or a resolved map layer identifier. A mismatch rejects the document before target resolution or mutation.

## Modification fields

| Field | Type | Requirement |
| --- | --- | --- |
| `id` | string | Required, non-empty, and unique within the document. |
| `target` | object | Required. Must contain `scene`, `path`, and `component`. Optional `structure` is a string. |
| `placement` | object | Optional. A fixed placement contains `mode`, `position` and `rotation`; a track-following placement contains `mode`, `track`, `offset` and `rotation`. See Placement below. |
| `active` | boolean | Optional. `false` disables the object, `true` enables it. Generic activity requires patcher 0.2.0 or later; track-marker activity requires 0.5.0 or later. |
| `relationships` | object | Optional. Explicit relationship settings; see Relationships below. Requires patcher 0.5.0 or later. |
| `dependsOn` | array of strings | Required. Each value names another modification in the same document. |

The target object accepts only `scene`, `path`, `component`, and optional `structure`. All required target strings must be non-empty. The scene adapter must resolve the identity to exactly one object. Zero or multiple matches fail the modification.

The order of the entries in `modifications` has no meaning. The engine applies each modification after every modification its `dependsOn` names, whatever their positions in the array. A modification whose dependency fails is not applied; modifications that do not depend on it still apply.

A modification holds the desired state of one object and must contain `placement`, `active`, `relationships`, or a combination. An omitted member leaves that part of the object untouched by this MapMod. An explicit `active` value is applied on every application, so a later activation by the game does not survive the next application of a document that disables the object. Patcher 0.1.0 rejects a modification that contains `active`; the dependency minimum below prevents that combination.

The [editor inventory](inspect-scene-objects.md) supplies observed identifiers and placement without changing this format. An inspection-only entry does not enable a new v1 operation. The engine rechecks the resolved object and map context immediately before mutation. A missing, ambiguous or replaced target fails rather than redirecting the operation. Editor integrations can bind a complete-document edit to its inspected observations through an in-memory token; that token is not serialized or distributed.

[World selection](select-world-targets.md) confirms scene-object identities for the existing `target` fields. Segment and node choices record their native IDs and loaded map context in the editor session. Version 1 stores fixed and track-following placement, activity, track-marker relationships and crossing coverage. A pending choice or confirmed track or node identity stays in the editor session. Applying an edit and using the editor's Save writes the authored state.

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

The engine reads the track's native pose in map coordinates when it applies the modification. For a segment location this is the track graph's position and rotation at `distance` metres from the named end. The frame's forward axis (+z) points along the segment away from that end: for `end` `a` towards end `b`, for `end` `b` towards end `a`. For an endpoint the frame is the node's own native position and rotation. The object's position is the track position plus `offset` turned by the track rotation; its rotation is the track rotation followed by `rotation`. Applying the same document again yields the same pose; offsets do not accumulate.

A segment or node that is missing, or a distance outside the segment, fails the modification before mutation, and the object keeps its pose. The engine never substitutes another track. Every application evaluates the track's current pose, so a world load after the track moved places the object at its new position relative to the moved track. In the map editor only the active edited MapMod's document applies, and the patcher also moves its following objects while their track moves; see [follow a track or endpoint](following-track.md). In ordinary play, every applicable document evaluates the track's current pose at world load. The stored `offset` and `rotation` do not change when the track moves.

## Relationships

The `relationships` object accepts `trackMarker` and `crossingCoverage`, and must contain at least one of them. Each relationship has three settings:

| Setting | Meaning |
| --- | --- |
| Omitted | The relationship stays as it is. This MapMod does not touch it. |
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

`crossingCoverage` requires patcher 0.5.0 or later. It is a non-empty array with one entry per explicitly set circuit of the target crossing and one entry per detection addition, described under Detection additions below. An entry for a circuit contains `circuit` and either the three spans or a detach:

| Field | Type | Requirement |
| --- | --- | --- |
| `circuit` | string | Required and non-empty. The ID of the circuit's native island span; it must name a circuit of the target crossing. Each circuit appears at most once. |
| `approachA`, `island`, `approachB` | object | Required together, unless the entry detaches. Each holds `from` and `to`, both track locations with the `segment`, `end` and `distance` fields above. |
| `detach` | boolean | `true`, alone with `circuit`. The crossing stops evaluating the circuit. |

The three settings apply per circuit. An omitted circuit keeps its effective coverage, and an omitted `crossingCoverage` leaves every circuit untouched. Selected tracks replace the circuit's spans with spans the patcher owns, covering the route from `from` to `to`; `from` is the native lower end, which lies next to the island for each approach. A detached circuit leaves the crossing's evaluation. In both cases the circuit's native spans stay registered and unchanged, so a still-valid old route stops triggering the crossing without being removed. Circuits keep their identity and native order.

The engine checks every entry before mutation: the circuit must exist on the crossing, each segment must exist exactly once, each distance must lie inside its segment and no span may have zero length. At least one circuit of the crossing must keep detection: a modification whose result, together with the crossing's current effective coverage, detaches every circuit is refused. A refusal leaves the crossing's coverage unchanged. Unknown fields in an entry make the modification unsupported; an entry with `detach` and span fields, `"detach": false`, an empty array and a partial set of spans are invalid.

#### Detection additions

A detection addition makes the target crossing detect explicitly selected extra tracks. It is an authored addition: a circuit the patcher builds and owns, beside the crossing's native circuits. An entry for an addition contains exactly these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `addition` | string | Required and non-empty. The addition's ID. It is unique in the document and scoped to the supplying MapMod. |
| `approachA`, `island`, `approachB` | object | All required. Each holds `from` and `to`, both track locations with the `segment`, `end` and `distance` fields above. |

The ID is the addition's persistent identity. The patcher joins it with the supplying MapMod's pack identifier, so two MapMods that choose the same ID get separate additions, and each application of a document finds the addition it built before. An ID may appear in one modification of a document. When two modifications use the same ID, both fail with that reason, whichever comes first in the file.

The three spans state the addition's track membership and detection area. The segments they name are the tracks that get detection, and the distances bound the area. The engine takes neither from track near the crossing. An entry never contains `circuit` or `detach`. An addition exists while the document lists it. Removing the entry deletes the addition.

The engine checks an addition before mutation like selected tracks: each segment must exist exactly once, each distance must lie inside its segment and no span may have zero length. An addition counts as detection for the rule that a crossing keeps at least one detecting circuit. A document result that leaves no detecting circuit or addition is refused.

For each addition the engine builds one circuit with three owned spans, registers the spans in the track graph and adds the circuit to the controller after its native circuits. Added circuits follow each other in the order of their identity, not the order of the entries. A repeated application reuses the circuit and every span whose tracks are unchanged, and replaces the spans of a role whose tracks changed. Deleting an addition removes its circuit from the controller, unregisters its spans and destroys them. Native circuits and other additions keep their state.

[Explicit snapping](snapping-to-track.md) writes a fixed placement. The editor computes a point on the explicitly selected segment's cubic curve or uses the selected node's position, preserving rotation, activity and logical binding. The graph selection and geometry checks belong to authoring preflight and are not serialized. Runtime application and reload use the resulting absolute pose without querying a snap target. Snapping adds no dependency minimum; later track movement does not change the saved pose. Track-following placement is the separate mode above.

## Several MapMods

During ordinary play the runtime considers each loaded pack that has `Data/scene-patches.json` and lists `crankyant/scene-patcher` under `dependencies.required` in its `Info.json`. It processes those documents in Railroader's load order, the order of the game's loaded pack list. Map-layer order plays no part, and the document has no priority field. A document whose `sourceMap.mapId` matches neither the selected Map definition nor one of its resolved map layers is skipped without a warning. In the map editor only the active edited MapMod's document is applied, as described above.

A failed document or modification does not undo the work of documents processed before it and does not stop the documents after it. An unreadable document or unreadable `Info.json` skips that pack as a whole.

When documents of different MapMods each modify the same existing object, the runtime adds one informational game message for that object. It names the object as `scene/path [component]` and the MapMods with their identifiers in load order, and states any declared dependency among them. A further MapMod on the same object updates that message. The message reports a possible incompatibility, not a confirmed conflict: application continues in load order, no change is undone, and the later document's values are applied last. A declared dependency does not suppress the message. A target that resolves to no object or to several objects, or modifications from one MapMod alone, produce no message. The message is not a failure report and adds no JSON fields.

## Runtime failure reports

The runtime reports a failed document or modification in a structured game warning, separate from the historical file log. A modification warning identifies the source as Railroader Scene Patcher and includes the supplying MapMod name, identifier and version, `Data/scene-patches.json`, operation, target when resolved, error, patcher version and game version. The warning also includes the supplied author and an HTTP or HTTPS website when present. It does not invent contact details.

Invalid supplied definitions are reported to the MapMod author. Unknown failures and demonstrated patcher faults are reported to the patcher maintainer. The runtime reports a given source and operation once per loaded world. Invalid or unsupported document content that requires recovery remains an editor matter; this reporting behavior adds no JSON fields or format version.

## Current native targets

### Independent board

The native adapter recognizes independent boards. The target `scene` and `path` name the board object, its `component` is `Helpers.Culling.RendererCuller`, and `structure`, when present, must be `independent-board-v1`. The object must carry that culler and exactly one `Map.Runtime.MaskComponents.CircleMapMask`, both enabled, with active parents. The adapter refuses a mask that flattens terrain height and a hierarchy with a `Track.*` component, another culler, another mask component, another culling handler or a missing script. The board itself may be disabled through `active`. The adapter requires the map conversion, culling, mask, terrain tile and terrain completion functions it uses. A board placement has no track binding.

After setting the board pose, the adapter requests a culling-position update and rebuilds the circle map mask. It then verifies that the culling sphere and every mask registration are at the board's position, or, for a disabled board, that neither registration remains; a mismatch fails the modification and restores the captured pose, activity and registrations. The game regenerates terrain output at the old and new positions. The adapter reports each position complete after a tile-built notification covering it and idle terrain work with no pending invalidations. A position whose terrain tile is not built needs no rebuild. A position that has not completed after 30 seconds is reported with the completed and outstanding positions; the modification stays applied. Save does not write the generated terrain output; the game regenerates it from the saved placement. Native edited terrain tiles are separate and are not changed by a board modification.

After a failed mutation whose restoration verified, later modifications of the same board fail until the restored terrain output completes. If that output stops, or restoration cannot be verified, they fail for the rest of the load.

An isolated native test on Railroader 0.6.2 build 26335745 observed the rendered Sylva board, updated culling sphere and registered mask at the desired pose, with settled terrain output; its sampled tree regions were empty. A separate research trial on the same build moved the Sylva board's mask onto one generated tree, observed the tree leave the generated tile output and return after restoration. Other boards qualify through the component pattern without individual trials.

The bridge, track-marker, crossing-pad and crossing targets below have their own adapters; other object families are not implemented.

### Bridge

The bridge adapter recognizes the Ela bridge root at `World/Large Scenery/Ela/Ela Bridge`, with target `component` `Helpers.Culling.RendererCuller`. Set `structure` to `bridge-colliders-v1`; the editor writes it. Use the `scene` value from the inspected identity. A modification may set `placement`, `active`, or both, as for the board.

The adapter moves and enables or disables the bridge root. Its colliders and renderer cullers move with it; the adapter refreshes the culling position of every active culler in the hierarchy and synchronises the physics scene. The parents of the root and its root culler must be active, the hierarchy must contain at least one collider, no rigidbody and no track-owned component, and no construction-scenery entry may switch the root. Before mutation, the collider identities, shapes, enabled flags and positions relative to the root must equal their first observation in the load.

After a change the adapter verifies the root pose and activity, the collider layout, collider activity under the root, and that the loaded generated trestles within 250 m of the bridge and the construction-scenery controller that references the bridge are unchanged. Separate track-owned scenery is never part of the moved hierarchy. A difference fails the modification and restores the captured bridge state; an unverified restoration blocks further operations on the bridge for the load. See [move or disable a bridge with its collision](placing-bridges.md) for the editor route and limits.

### Track marker

The track-marker adapter recognizes an object holding one Generic `Track.TrackMarker`. The target `component` is `Track.TrackMarker`; set `structure` to `track-marker-v1`. The object must not be a milepost, a crossing pad or part of a crossing; no other component may listen to the marker's location changes; a `Track.TrackRelativePosition` whose activity follows a track group must activate this object itself, and no other component may have replaced its activation callback; no loaded component other than that relative position may hold a direct reference to the marker, and a scan that cannot read a field able to hold a marker refuses every marker; and no new-game car placement may use it. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination.

A `trackMarker` setting writes the marker's serialized location and its named-location registration in the track graph: the adapter removes the registration under the marker ID, writes the location, and registers the marker at the new location when it is enabled and active. A detach leaves no registration. Map labels below the marker are registered with the map again, so their dimming follows the new location's track group. Placement moves the object root and refreshes its renderer cullers; it never changes the binding. Before mutation the adapter requires one registration of this marker at its current location for an enabled, active marker with a resolvable location, and none otherwise. After a change it verifies the location, the pose, the activity, that registration rule and the map labels' track group. A difference fails the modification and restores the captured location, registration, pose and activity; an unverified restoration blocks further operations on the marker for the load. See [repair, replace or detach a track-marker binding](repairing-track-bindings.md) for the editor route and limits.

#### Group-following markers

A marker object whose `Track.TrackRelativePosition` has its activity follow the track group subscribes to the track graph's group event for the group of its bound segment, and the game activates or deactivates the object with that group. For such an object:

- A `trackMarker` setting also moves that subscription: after the location change the adapter runs the relative position's own unsubscribe and observe steps, so exactly one subscription observes the new segment's group. A detach, or a location that does not resolve, leaves no subscription.
- `active: false` is an explicit disable. The adapter replaces the relative position's activation callback with a hold that keeps the object inactive, before anything else changes, so a later group activation cannot activate it, not even briefly, including during a repeated application.
- An omitted `active` keeps the game's group-driven activity: the group activates and deactivates the object.
- `active: true` is refused before mutation, because the object's activity belongs to its track group. Omit `active` to keep group-driven activity.
- Removing the disable, by clearing `active` or removing the modification, releases the hold and observes the group again, so the object's activity follows the group's current state.

After a change the adapter also verifies the subscription count, the observed group and the activity the group or the hold requires. A difference fails the modification and restores the captured location, registration, subscription, hold, pose and activity; an unverified restoration blocks further operations on the marker for the load.

### Crossing pad

The crossing-pad adapter recognizes an object holding one `Track.TrackMarker` of type `Crossing` or `Generic` and one `Track.TrackCurveMeshBuilder` with a mesh container and a mesh profile. The target `component` is `Track.TrackMarker`; set `structure` to `crossing-pad-v1`. A pad target named with `track-marker-v1` does not resolve, and a Generic-marker target never resolves to a pad. The consumer rules of the track-marker adapter apply, except that the pad builder may hold the marker, a pad inside a crossing qualifies, and a pad whose activity follows a track group is refused. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination.

A `trackMarker` reference on a pad moves the marker's location and graph registration as for a Generic marker, then runs the game's pad generation. Generation rebuilds the pad mesh along the track at the new location and moves the culling sphere to the new bounds; the game's culling recomputes the mesh visibility on its next update. `{ "detach": true }` fails the modification before mutation, because a pad without a location has no geometry; disable the pad with `"active": false` to hide it. A placement change also regenerates the pad, so the mesh stays on the track while the object moves. For a pad with `placement.mode` `trackFollowing`, the adapter also records the track curves the pad was generated from, the segment curves within half the pad width on either side of the marker location, and regenerates the pad when they change, even if its pose does not; the editor checks this with its track-following reconciliation, including after native Undo and Redo of track edits, and regenerates once the changed curves have held still between two checks. A pad without track-following placement is not regenerated after a track edit by itself; every application of a pad modification, and its removal, regenerates the pad when the recorded source curves differ from the current track. After a change the adapter verifies the track-marker rules, that the current mesh was generated from the authored location and the pad's current position, and that the culling sphere encloses that mesh. A pad generated by the game before the patcher observed it is generated once more on the first application, to the same mesh; later applications leave current geometry alone. A difference fails the modification and restores the captured location, registration, pose and activity, generates the pad again and requires the regenerated mesh to match the captured one; an unverified restoration blocks further operations on the pad for the load. One native journey on Railroader 0.6.2 with patcher commit `a282015` observed the pad Mg74 shift along the track after a rebinding, refusal of a missing segment and an out-of-range distance, native Undo and Redo, removal, and Save with an editor reload that reproduced one modification and one registration. A second journey with commit `ab2816f` measured that the rebinding changed the pad's vertex hash and moved its culling sphere 0.993 metres along the track while the sphere stayed aligned with the new bounds, and that native Undo and Remove modification restored the original vertex hash, bounds and culling sphere exactly. See [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md) for the editor route and limits.

### Crossing

The crossing-coverage adapter recognizes an object holding one `Track.Crossings.GradeCrossingController` whose circuit list is non-empty and whose circuits each hold an approach A, island and approach B span with an ID. The target `component` is `Track.Crossings.GradeCrossingController`; set `structure` to `crossing-coverage-v1`. A modification may set only `relationships.crossingCoverage`; `placement`, `active` and `trackMarker` are refused before mutation.

The adapter records the crossing's circuits and their native spans at its first observation in the load, which requires every native span to be registered in the track graph under its ID. A selected circuit gets three spans owned by the patcher, created disabled under a root that is not saved, given their ID and endpoints and enabled so that the native span lifecycle registers them; a repeated application reuses matching owned spans. The circuit then refers to them. A detached circuit is removed from the controller's circuit list and keeps its native references; the controller evaluates the remaining circuits in their native order from its next pass. A detection addition gets a new circuit set with three owned spans, built the same way, and the controller's list ends with the added sets. The adapter remembers each set it adds by the set's own identity, so an adapter created later over a world that still holds the set reconciles it and does not build a second one. Owned spans that no circuit uses are disabled, which removes their registration, and destroyed.

Before mutation the adapter requires that the controller evaluates only its native circuits in native order followed by the circuits it added, that each native circuit holds its native spans or owned spans, and that every native span stays registered. After a change it verifies each circuit's coverage, each addition's tracks, that no other added circuit remains, those rules, and that exactly the owned spans in use are registered and cover track. A difference fails the modification and restores the captured coverage, which removes a partly built addition and returns the captured additions; owned spans that stay registered after restoration make cleanup unverified and block further operations on the crossing for the load. The map editor's Save writes neither the controller's references nor the added circuits nor the owned spans, so each load rebuilds them from the document. See [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md) and [add detection for extra tracks at an existing crossing](adding-crossing-detection.md) for the editor route and limits.

## Writing by the editor

The editor writes the document as indented UTF-8 JSON with line-feed line endings and no byte-order mark. It keeps the member order of the source, writes one modification per edited object, and gives a new modification the `id` `place-<object name>`, `bind-<object name>` when its first edit is a track-marker relationship, `cover-<object name>` when it is crossing coverage, or `follow-<object name>` when its first edit is track following, made unique with a numeric suffix. Whitespace and comments of the source are not kept.

Unknown fields at the document root and unknown modifications are preserved without being executed. Every number the editor did not replace keeps its source text, so a value such as `0.10` or a number beyond double precision is written back unchanged. The engine rejects unknown fields inside a modification, target, placement or relationship when it tries to execute that modification; the editor preserves such a modification and does not edit or remove it. A document with a missing, malformed, or unsupported format version, a document for another map, and a document whose numbers cannot be matched to their source text are protected: the editor does not write them.

## Dependency minimum

The supplying MapMod's `Info.json` must list `crankyant/scene-patcher` under `dependencies.required`; the runtime skips the document of a pack that does not. The editor's Save adds or raises that entry.

| Document content | Minimum patcher version |
| --- | --- |
| Fixed placement only | `0.1.0` |
| Any modification with `active` | `0.2.0` |
| An independent-board target other than the legacy Sylva location sign | `0.5.0` |
| Any modification whose target `structure` is `bridge-colliders-v1` | `0.5.0` |
| Any modification with `relationships`, a `Track.TrackMarker` target component, or target `structure` `track-marker-v1` | `0.5.0` |
| Any modification with `active` whose target `structure` is `track-marker-v1` | `0.5.0` |
| Any modification whose `placement.mode` is `trackFollowing` | `0.5.0` |
| Any modification with `relationships.crossingCoverage`, including a detection addition, a `Track.Crossings.GradeCrossingController` target component, or target `structure` `crossing-coverage-v1` | `0.5.0` |
| Any modification whose target `structure` is `crossing-pad-v1` | `0.5.0` |

Fixed placement is the base minimum; other content in the document can raise it according to the table. The minimum follows the document content, whether or not the target is loaded or its capability is available in this editor. The legacy Sylva location sign does not raise the pattern-based board dependency minimum. Save writes `crankyant/scene-patcher >=<minimum>` without an upper bound. It leaves an entry unchanged when a constraint already requires that version or a later one, keeps other constraints of the entry, and does not lower a minimum. When an existing constraint excludes the required minimum, Save reports the conflict and does not write the entry. Runtime reporting adds no document, schema or capability requirement.

Duplicate JSON property names are rejected during parsing. Duplicate modification IDs reject the document. A target whose `structure` names a capability that is incompatible with the running game build fails with that reason; other modifications still apply. A target preflight or mutation failure is recorded for the current load and is not retried during that load. If mutation cleanup cannot be verified, later modifications that resolve to that target fail closed.

## Application on each peer

Format version 1 has no multiplayer fields. The host and every client read `Data/scene-patches.json` from their own installed copy of the MapMod and apply it to their own scene. `sourceMap.mapId`, target identity and capability prerequisites are checked on each peer. `placement.position` is in map coordinates and is converted through each peer's own world origin. A repeated world-load callback reconciles the stored placement without accumulating offsets and does not retry failed modifications. A later load of the same map, including reconnect and same-map re-entry, starts a new load attempt. A target that still resolves under its identity keeps its applied state and any unverified-cleanup guard. A target that the game rebuilt gets the modification as new, and the patcher removes only what it owned for the old object, such as the spans of a detection addition. Failed modifications are retried once per load attempt. A cleanup failure is reported on that peer only and retried at the next load. Modification `id` values and addition IDs are the persistent identities across load attempts. In single-player, loading the same save again in one process rebuilt every patched object, and each modification was applied once. The native behavior on reconnect has not been checked.

## Minimal example

```json
{
  "formatVersion": 1,
  "sourceMap": { "mapId": "example-map-id" },
  "modifications": [
    {
      "id": "place-object",
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
  ]
}
```

## Transform gizmo authoring

A completed [transform gizmo gesture](using-transform-gizmos.md) writes the same fixed placement as numeric authoring. Position remains in map coordinates and rotation uses quaternion order. Preview frames are temporary and do not appear in the document. The gesture creates no logical binding. On an object that follows a track, the gesture's result is stored as a new `offset` and `rotation` relative to the same track. Native Save writes the active edited MapMod's canonical document and dependency metadata; Undo and Redo reverse completed gestures through native history.
