<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene-patch document v1

This reference defines the v1 JSON fields accepted by the core engine for fixed and track-following placement, activity and track-marker relationships. Each supplying MapMod keeps one document at `Data/scene-patches.json` in its own pack, as strict UTF-8; a leading byte-order mark is accepted. The runtime reads it and never writes it. The map editor writes it for the active edited MapMod, as described in [edit and save fixed placement in the map editor](editing-placement-in-the-editor.md). Current native target support covers independent boards, described in [move a board with its terrain effects](moving-boards.md), the Ela bridge, described in [move or disable a bridge with its collision](placing-bridges.md), and Generic track markers, described in [repair, replace or detach a track-marker binding](repairing-track-bindings.md). The native observations and their limits are described under Current native targets below. The runtime capability classification remains `Unchecked`.

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
| `active` | boolean | Optional. `false` disables the object, `true` enables it. Requires patcher 0.2.0 or later. |
| `relationships` | object | Optional. Explicit relationship settings; see Relationships below. Requires patcher 0.4.0 or later. |
| `dependsOn` | array of strings | Required. Each value names another modification in the same document. |

The target object accepts only `scene`, `path`, `component`, and optional `structure`. All required target strings must be non-empty. The scene adapter must resolve the identity to exactly one object. Zero or multiple matches fail the modification.

A modification holds the desired state of one object and must contain `placement`, `active`, `relationships`, or a combination. An omitted member leaves that part of the object untouched by this MapMod. An explicit `active` value is applied on every application, so a later activation by the game does not survive the next application of a document that disables the object. Patcher 0.1.0 rejects a modification that contains `active`; the dependency minimum below prevents that combination.

The [editor inventory](inspect-scene-objects.md) supplies observed identifiers and placement without changing this format. An inspection-only entry does not enable a new v1 operation. The engine rechecks the resolved object and map context immediately before mutation. A missing, ambiguous or replaced target fails rather than redirecting the operation. Editor integrations can bind a complete-document edit to its inspected observations through an in-memory token; that token is not serialized or distributed.

[World selection](select-world-targets.md) confirms scene-object identities for the existing `target` fields. Segment and node choices record their native IDs and loaded map context in the editor session. Version 1 stores fixed and track-following placement, activity and track-marker relationships. A pending choice or confirmed track or node identity stays in the editor session. Applying an edit and using the editor's Save writes the authored state.

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

The `relationships` object accepts only `trackMarker`. Each relationship has three settings:

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

[Explicit snapping](snapping-to-track.md) writes a fixed placement. The editor computes a point on the explicitly selected segment's cubic curve or uses the selected node's position, preserving rotation, activity and logical binding. The graph selection and geometry checks belong to authoring preflight and are not serialized. Runtime application and reload use the resulting absolute pose without querying a snap target. Snapping adds no dependency minimum; later track movement does not change the saved pose. Track-following placement is the separate mode above.

## Current native targets

### Independent board

The native adapter recognizes independent boards. The target `scene` and `path` name the board object, its `component` is `Helpers.Culling.RendererCuller`, and `structure`, when present, must be `independent-board-v1`. The object must carry that culler and exactly one `Map.Runtime.MaskComponents.CircleMapMask`, both enabled, with active parents. The adapter refuses a mask that flattens terrain height and a hierarchy with a `Track.*` component, another culler, another mask component, another culling handler or a missing script. The board itself may be disabled through `active`. The adapter requires the map conversion, culling, mask, terrain tile and terrain completion functions it uses. A board placement has no track binding.

After setting the board pose, the adapter requests a culling-position update and rebuilds the circle map mask. It then verifies that the culling sphere and every mask registration are at the board's position, or, for a disabled board, that neither registration remains; a mismatch fails the modification and restores the captured pose, activity and registrations. The game regenerates terrain output at the old and new positions. The adapter reports each position complete after a tile-built notification covering it and idle terrain work with no pending invalidations. A position whose terrain tile is not built needs no rebuild. A position that has not completed after 30 seconds is reported with the completed and outstanding positions; the modification stays applied. Save does not write the generated terrain output; the game regenerates it from the saved placement. Native edited terrain tiles are separate and are not changed by a board modification.

After a failed mutation whose restoration verified, later modifications of the same board fail until the restored terrain output completes. If that output stops, or restoration cannot be verified, they fail for the rest of the load.

An isolated native test on Railroader 0.6.2 build 26335745 observed the rendered Sylva board, updated culling sphere and registered mask at the desired pose, with settled terrain output; its sampled tree regions were empty. A separate research trial on the same build moved the Sylva board's mask onto one generated tree, observed the tree leave the generated tile output and return after restoration. Other boards qualify through the component pattern without individual trials.

The bridge and track-marker targets below have their own adapters; other object families are not implemented.

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

## Writing by the editor

The editor writes the document as indented UTF-8 JSON with line-feed line endings and no byte-order mark. It keeps the member order of the source, writes one modification per edited object, and gives a new modification the `id` `place-<object name>`, `bind-<object name>` when its first edit is a relationship, or `follow-<object name>` when its first edit is track following, made unique with a numeric suffix. Whitespace and comments of the source are not kept.

Unknown fields at the document root and unknown modifications are preserved without being executed. Every number the editor did not replace keeps its source text, so a value such as `0.10` or a number beyond double precision is written back unchanged. The engine rejects unknown fields inside a modification, target, placement or relationship when it tries to execute that modification; the editor preserves such a modification and does not edit or remove it. A document with a missing, malformed, or unsupported format version, a document for another map, and a document whose numbers cannot be matched to their source text are protected: the editor does not write them.

## Dependency minimum

The supplying MapMod's `Info.json` must list `crankyant/scene-patcher` under `dependencies.required`; the runtime skips the document of a pack that does not. The editor's Save adds or raises that entry.

| Document content | Minimum patcher version |
| --- | --- |
| Fixed placement only | `0.1.0` |
| Any modification with `active` | `0.2.0` |
| A board target other than `World/Signs/Loc: Sylva Int` in `BushnellWhittier-MapManager` | `0.3.0` |
| Any modification whose target `structure` is `bridge-colliders-v1` | `0.3.0` |
| Any modification with `relationships`, or whose target `structure` is `track-marker-v1` | `0.4.0` |
| Any modification whose `placement.mode` is `trackFollowing` | `0.5.0` |
| Any modification with `active` whose target `structure` is `track-marker-v1` | `0.5.0` |

The minimum follows from the document content alone, whether or not the target is loaded or its capability is available in this editor. An `active` value on a track-marker target can be the explicit disable of a group follower, which patcher 0.4.0 refuses; patcher 0.4.0 rejects a `trackFollowing` placement as unsupported, so only that modification would fail. Save writes `crankyant/scene-patcher >=<minimum>` without an upper bound. It leaves an entry unchanged when a constraint already requires that version or a later one, keeps other constraints of the entry, and does not lower a minimum. When an existing constraint excludes the required minimum, Save reports the conflict and does not write the entry.

Duplicate JSON property names are rejected during parsing. Duplicate modification IDs reject the document. A target whose `structure` names a capability that is incompatible with the running game build fails with that reason; other modifications still apply. A target preflight or mutation failure is recorded for the current load and is not retried during that load. If mutation cleanup cannot be verified, later modifications that resolve to that target fail closed.

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
