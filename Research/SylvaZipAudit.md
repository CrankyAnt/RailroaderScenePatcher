# SylvaInterchange ZIP audit

## Scope and result

Read `<USER_PROFILE>\Downloads\Katers-SylvaInterchange.zip` directly through Python `zipfile`; no extraction, install, save, or mod edits were performed. The archive contains the expected four files plus directory entries: `Info.json` (203 bytes), `Definitions.json` (484), `Data/Map.json` (61,541), and `Scripts/Data.rl` (84).

The archive is a native 0.5.0 map overlay declaring `rr/game >=0.6.0`. Its script is only `map.merge(json.load("Data/Map.json"))`; the archive contains no DLL. This is evidence that the archived behavior is expressed as JSON map patches and the tiny loader script, not evidence that every behavior seen in a different mod is JSON-only. The separate Sox mod includes `CrankyAnt.ElaMarkerRepair.dll` (7,680 bytes), but this ZIP does not.

## Native JSON structure and exact patch examples

`Definitions.json` has one accepted top-level section, `objects` (array). Its sole definition identifies `connor/basegame1`, `kind: "Map"`, `mapData: "./Map.json"`, terrain set `rr/nc-bryson`, layer `rr/bushnell-whittier`, and empty `components: {}`. `Info.json` fields are `identifier`, `name`, `author`, `version`, and `dependencies`; the required dependency is `rr/game >=0.6.0`.

`Data/Map.json` has two top-level sections, `graph` and `objects` (there are no `ops` or `folders` sections in this overlay). `graph` accepts `nodes`, `segments`, and `turntables` here. Counts are 191 node entries, 183 segment entries, and one turntable. Representative direct map changes:

- `graph.nodes.N7w9` replaces a base node transform with position `[25392.7363, 627.851, -211.804]` and rotation `[-0.8429, 18.9167, 0.0]`.
- `graph.nodes.N8f9` is a partial patch `{ "flipSwitchStand": true }`; the base node has position and rotation too. This demonstrates a component/flag-style JSON patch without replacing the whole node.
- `graph.segments.Sd6n` is `{ "flags": 16 }` over a base segment whose endpoints/group are `Nqou`, `Nm7d`, and `e-c1` respectively. This is also a partial segment patch.
- `graph.turntables.KatSylInt_turntable1` carries `position`, `rotation`, `radius`, `subdivisions`, `nodeIds` (32), `defaultStopIndex`, and `bridgeGroupId`.

Of the 27 object IDs, 23 are typed scene objects (`Prop`, `Builtin`, `CircleMask`, `CurveMask`, `RectangleMask`) and four are untyped geometry payloads. The typed objects use ordinary serialized fields including `kind`, `identifier`/`builtinId`, `position`, `rotation`, `scale`, `folder`, and where applicable `params` or mask parameters. No object carries `enabled`, `disabled`, `components`, or a scene marker kind. There is no separate scene-component patch section in the archive.

Concrete added scene content beyond Sox's Ela patch includes `OKatSylInt_allen-dual-shed` (`Prop`, `rr/allen-dual-shed`), four deck `Prop` objects (`rr/deck-modular-10ft-by-40ft`), and two `Builtin` diesel fueling stands; one has `params: {"supply":"industry"}`. Six `Builtin` objects demonstrate serialized rail anchors, for example `OKatSylInt_diesel-fueling-stand-2` has `anchor.location: "SKatSylInt_dzvf|a|10.47284"`, a 3-value `offset`, `angle: 0.385986328`, and `snapToTerrain: false`. The anchored segment ID is included in this overlay's graph. This provides a native JSON example of anchoring scene objects to track locations.

## Base-map merge and tombstones

The base `nc-bryson-terrain/Data/Map.json` has `graph`, `ops`, `objects`, and `folders`. This overlay omits `ops` and `folders`, so the archive supplies no replacement or tombstone for those sections. It shares 39 node IDs, 26 segment IDs, and four object IDs with the current base file. All 39 shared nodes and all 26 shared segments differ from base, consistent with an overlay that updates extant entries as well as adding its own IDs.

Explicit JSON `null` values are deletion markers in the overlay: six existing nodes are null (`N6ip`, `N8sd`, `Nocj`, `Np66`, `Nsgv`, `Ntky0oxjf`) and 19 existing segments are null (including `S0gi`, `Sgoj`, `Sgru`, `Sch6`, `Stky0i21d`, `Sv6m`). This is a concrete JSON-only pattern for deleting stale rail graph entries. Example: base `graph.segments.Sgoj` joins `Nll7` to `Ncxm` in group `sylva-paperboard`; overlay `graph.segments.Sgoj: null` removes it. In contrast, many entries with the same base ID are replacements, not deletions.

The overlay also supplies new rail graph IDs and object anchors. Do not copy these literal IDs blindly into another map: several are local to this map, and one overlay segment (`SKatSylInt_tofd`) points to `Ndup`, which is absent from the overlay but exists in base; that is valid only in the context of the merged base graph. Therefore a repair can plausibly patch known changed/deleted graph IDs, partial node flags, segment flags, and anchored object properties in JSON, but the target's actual old/new IDs and component schema must be verified against that target map. This archive does not document or prove a generic mechanism for discovering renamed track-marker IDs automatically.

## Comparison with Sox mod

Sox's `Data/Map.json` uses the same general outer native shape (`graph`, `ops`, `objects`, `folders`) and its graph contains nodes and segments. Its payload has 41 object entries and the `ops.areas` industry structure. The Sylva ZIP's extra evidence is principally the partial node/segment patches, null deletion tombstones, turntable schema, and track-anchored Builtin objects; the ZIP has no `ops` industry configuration and no explicit scene component/marker object.
