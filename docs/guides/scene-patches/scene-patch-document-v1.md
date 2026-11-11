<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene-patch document v1

This reference defines the v1 JSON fields accepted by the core engine for fixed placement and activity. Each supplying MapMod keeps one document at `Data/scene-patches.json` in its own pack, as strict UTF-8; a leading byte-order mark is accepted. The runtime reads it and never writes it. The map editor writes it for the active edited MapMod, as described in [edit and save fixed placement in the map editor](editing-placement-in-the-editor.md). Current native target support is limited to the independent Sylva board described in [author fixed scene placement](authoring-fixed-placement.md) and the Ela bridge described in [move or disable a bridge with its collision](placing-bridges.md). The native observations and their limits are described under Current native targets below. The runtime capability classification remains `Unchecked`.

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
| `placement` | object | Optional. Must contain `mode`, `position`, and `rotation`. |
| `active` | boolean | Optional. `false` disables the object, `true` enables it. Requires patcher 0.2.0 or later. |
| `dependsOn` | array of strings | Required. Each value names another modification in the same document. |

The target object accepts only `scene`, `path`, `component`, and optional `structure`. All required target strings must be non-empty. The scene adapter must resolve the identity to exactly one object. Zero or multiple matches fail the modification.

A modification holds the desired state of one object and must contain `placement`, `active`, or both. An omitted member leaves that part of the object untouched by this MapMod. An explicit `active` value is applied on every application, so a later activation by the game does not survive the next application of a document that disables the object. Patcher 0.1.0 rejects a modification that contains `active`; the dependency minimum below prevents that combination.

The [editor inventory](inspect-scene-objects.md) supplies observed identifiers and placement without changing this format. An inspection-only entry does not enable a new v1 operation. The engine rechecks the resolved object and map context immediately before mutation. A missing, ambiguous or replaced target fails rather than redirecting the operation. Editor integrations can bind a complete-document edit to its inspected observations through an in-memory token; that token is not serialized or distributed.

[World selection](select-world-targets.md) confirms scene-object identities for the existing `target` fields. Segment and node choices record their native IDs and loaded map context in the editor session. Version 1 stores fixed placement and activity. A pending choice or confirmed track or node identity stays in the editor session. Applying an edit and using the editor's Save writes the authored state.

The placement object accepts only `mode`, `position`, and `rotation`. `mode` must be `fixed`. `position` must contain three finite JSON numbers in map coordinates. `rotation` must contain four finite JSON numbers, representing a nonzero quaternion. The board and bridge adapters require a unit quaternion and perform the game map/world coordinate conversion. The adapter defines the meaning of optional `structure`. Placement is separate from logical binding: no v1 field binds an object to a track, and placing an object never changes its track relationships.

## Current native targets

### Independent board

The native adapter recognizes the independent Sylva board at `World/Signs/Loc: Sylva Int` in the `BushnellWhittier-MapManager` scene. Its target `component` is `Helpers.Culling.RendererCuller`; set `structure` to `independent-board-v1` to identify the supported pattern. The board, culler, and `Map.Runtime.MaskComponents.CircleMapMask` must be active. The adapter rejects unexpected `Track.*` descendants and requires the map conversion, culling, mask rebuild, and terrain completion hooks it uses.

After setting the board pose, the adapter requests a culling-position update and rebuilds the circle map mask. It reports dependent terrain work complete only after receiving tile-built notifications for both the old and new positions and observing idle terrain work with no pending invalidations. An isolated native test on Railroader 0.6.2 build 26335745 observed the rendered board, updated culling sphere and registered mask at the desired pose, with settled terrain output. Both sampled tree regions were empty, so the test does not demonstrate removal of an occupied tree. This adapter implements no other board pattern. The bridge below has its own adapter; other object families are not implemented.

### Bridge

The bridge adapter recognizes the Ela bridge root at `World/Large Scenery/Ela/Ela Bridge`, with target `component` `Helpers.Culling.RendererCuller`. Set `structure` to `bridge-colliders-v1`; the editor writes it. Use the `scene` value from the inspected identity. A modification may set `placement`, `active`, or both, as for the board.

The adapter moves and enables or disables the bridge root. Its colliders and renderer cullers move with it; the adapter refreshes the culling position of every active culler in the hierarchy and synchronises the physics scene. The parents of the root and its root culler must be active, the hierarchy must contain at least one collider, no rigidbody and no track-owned component, and no construction-scenery entry may switch the root. Before mutation, the collider identities, shapes, enabled flags and positions relative to the root must equal their first observation in the load.

After a change the adapter verifies the root pose and activity, the collider layout, collider activity under the root, and that the loaded generated trestles within 250 m of the bridge and the construction-scenery controller that references the bridge are unchanged. Separate track-owned scenery is never part of the moved hierarchy. A difference fails the modification and restores the captured bridge state; an unverified restoration blocks further operations on the bridge for the load. See [move or disable a bridge with its collision](placing-bridges.md) for the editor route and limits.

## Writing by the editor

The editor writes the document as indented UTF-8 JSON with line-feed line endings and no byte-order mark. It keeps the member order of the source, writes one modification per edited object, and gives a new modification the `id` `place-<object name>`, made unique with a numeric suffix. Whitespace and comments of the source are not kept.

Unknown fields at the document root and unknown modifications are preserved without being executed. Every number the editor did not replace keeps its source text, so a value such as `0.10` or a number beyond double precision is written back unchanged. The engine rejects unknown fields inside a modification, target, or placement when it tries to execute that modification; the editor preserves such a modification and does not edit or remove it. A document with a missing, malformed, or unsupported format version, a document for another map, and a document whose numbers cannot be matched to their source text are protected: the editor does not write them.

## Dependency minimum

The supplying MapMod's `Info.json` must list `crankyant/scene-patcher` under `dependencies.required`; the runtime skips the document of a pack that does not. The editor's Save adds or raises that entry.

| Document content | Minimum patcher version |
| --- | --- |
| Fixed placement only | `0.1.0` |
| Any modification with `active` | `0.2.0` |
| Any modification whose target `structure` is `bridge-colliders-v1` | `0.3.0` |

Save writes `crankyant/scene-patcher >=<minimum>` without an upper bound. It leaves an entry unchanged when a constraint already requires that version or a later one, keeps other constraints of the entry, and does not lower a minimum. When an existing constraint excludes the required minimum, Save reports the conflict and does not write the entry.

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
