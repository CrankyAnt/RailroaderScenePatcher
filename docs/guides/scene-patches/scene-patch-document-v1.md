<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene-patch document v1

This reference defines the v1 JSON fields accepted by the core engine for fixed placement. The native runtime reads `Data/scene-patches.json` as strict UTF-8 and does not write it. Current native target support is limited to the independent Sylva board described in [author fixed scene placement](authoring-fixed-placement.md). The native observations and their limits are described under Current native target below. The runtime capability classification remains `Unchecked`.

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
| `placement` | object | Required. Must contain `mode`, `position`, and `rotation`. |
| `dependsOn` | array of strings | Required. Each value names another modification in the same document. |

The target object accepts only `scene`, `path`, `component`, and optional `structure`. All required target strings must be non-empty. The scene adapter must resolve the identity to exactly one object. Zero or multiple matches fail the modification.

The [editor inventory](inspect-scene-objects.md) supplies observed identifiers and placement without changing this format. An inspection-only entry does not enable a new v1 operation. The engine rechecks the resolved object and map context immediately before mutation. A missing, ambiguous or replaced target fails rather than redirecting the operation. Editor integrations can bind a complete-document edit to its inspected observations through an in-memory token; that token is not serialized or distributed.

The placement object accepts only `mode`, `position`, and `rotation`. `mode` must be `fixed`. `position` must contain three finite JSON numbers in map coordinates. `rotation` must contain four finite JSON numbers, representing a nonzero quaternion. The board adapter requires a unit quaternion and performs the game map/world coordinate conversion. The adapter defines the meaning of optional `structure`.

## Current native target

The native adapter recognizes the independent Sylva board at `World/Signs/Loc: Sylva Int` in the `BushnellWhittier-MapManager` scene. Its target `component` is `Helpers.Culling.RendererCuller`; set `structure` to `independent-board-v1` to identify the supported pattern. The board, culler, and `Map.Runtime.MaskComponents.CircleMapMask` must be active. The adapter rejects unexpected `Track.*` descendants and requires the map conversion, culling, mask rebuild, and terrain completion hooks it uses.

After setting the board pose, the adapter requests a culling-position update and rebuilds the circle map mask. It reports dependent terrain work complete only after receiving tile-built notifications for both the old and new positions and observing idle terrain work with no pending invalidations. An isolated native test on Railroader 0.6.2 build 26335745 observed the rendered board, updated culling sphere and registered mask at the desired pose, with settled terrain output. Both sampled tree regions were empty, so the test does not demonstrate removal of an occupied tree. Other object families and board patterns are not implemented.

Unknown fields at the document root remain in the parsed document and are retained by `PatchDocument.Write`. The engine rejects unknown fields inside a modification, target, or placement when it tries to execute that modification. Documents with a missing, malformed, or unsupported format version cannot be written through `PatchDocument.Write`. The native runtime has no document save path, so it does not overwrite these documents.

Duplicate JSON property names are rejected during parsing. Duplicate modification IDs reject the document. A target preflight or mutation failure is recorded for the current load and is not retried during that load. If mutation cleanup cannot be verified, later modifications that resolve to that target fail closed.

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
