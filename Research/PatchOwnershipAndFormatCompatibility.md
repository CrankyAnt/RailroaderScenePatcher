<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Patch ownership and format compatibility lookup

This fact lookup supports [Choose the patch document and target-identity contract](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/9). It distinguishes the source of a retained scene object, the active edit destination and format compatibility. It does not resolve the ambiguity or compatibility policies.

## Existing ownership evidence

The [map-mod loading inspection](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/c7f2ce79ef1ef813d62ee78414f26a7eeb1b84ae/Research/MapModPatchLoading.md) distinguishes a map definition, its owning asset pack, resolved map data and inherited map layers. A definition can resolve data in another pack. Loaded or writable pack status alone does not identify the edit destination.

The [ownership and readiness trials](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/56dd11cd8f60a732c1796359362a8cef5da298ec/Research/OwnershipReadiness.md) identify B as the active edit definition and definition owner in the tested layered fixture, with inherited data in A and game core. B's writable store is an inferred candidate patch destination. This was not a patch-file write test and does not identify the original provider of a retained scene object.

The [candidate ownership report](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/f8b3a72f00dc6556e5d37894605b0312658ad598/Research/CandidateOwnership.md) records concrete scene membership, hierarchy, components and relationships for selected roots. It does not establish original asset serialization provenance. Scene membership and runtime component type do not by themselves prove the provider pack.

The [official map guide](https://docs.railroadergame.com/modding/map-modding/) distinguishes a complete new map and a map mod that modifies an existing map. Both distribute as asset packs. This does not itself establish how arbitrary retained Unity scene roots acquire provider metadata.

## Format version and distribution requirements

Sources checked on 2026-11-09:

- [Railroader Info.json reference](https://docs.railroadergame.com/modding/info-json/): required dependencies support version constraints such as greater-than-or-equal and combined ranges. This supplies native pack-level dependency metadata independently of a download service. This lookup did not empirically test a version refusal.
- [Nexus Mods file-level requirements and Vortex Health Check](https://www.nexusmods.com/site/news/15591): file-level requirements support specific files and version ranges; Vortex 2.6 can flag missing or out-of-range dependencies. This does not establish the installed Railroader/Vortex integration, automatic updates, or enforcement for manual installation.
- [Nexus author guidance](https://help.nexusmods.com/article/136-best-practices-for-mod-authors): mod-level requirements prompt downloads of prerequisites. They should not be conflated with native load-time version checks.

A patch format version describes the interpretation of patch data. It is separate from the patcher's release version and the consuming MapMod's release version. One patcher release can read several supported format versions. A newer emitted format can raise the minimum patcher requirement without requiring a separate player build and editor build.

## Installed scene-loading inspection

Read-only IL inspection on 2026-11-09 checked installed `Railroader.dll`, SHA-256 `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. No game was launched and no temporary vendor IL file was retained.

`UI.Menu.GlobalGameManager/<LoadMap>d__62::MoveNext` resolves a requested pack map identifier through `AssetPackManager.Shared.TryGetDefinitionInfo<MapDefinition>` and activates `Game.ActiveMapDefinition`. It separately selects a built-in Unity scene descriptor. The built-in `rr/bushnell-whittier` map is special-cased to `BushnellWhittier`. The inspected path checks inherited map-layer identifiers for that map, and can select `DataMap` for other pack maps when that scene is present in the build. If unavailable, the fallback warns that the pack map will run in the BushnellWhittier scene with baked content.

This distinguishes the active data/map-layer context from the loaded Unity scene. It does not establish that every scene object is serialized in the active map pack or identify an arbitrary object's original provider. A scene path/name/handle establishes scene membership, not automatically the originating asset pack. Whether some other creation path retains provider metadata remains unproven.

The inspected `Map.Runtime.MapStore` exposes `BasePath`, the terrain manifest name `Terrain.json`, and `MapPath()`. `LayeredMapStore` holds map-store layers. These are map-data stores, not proven universal provider records for retained Unity scene roots.

The active edited map definition and its writable owning pack remain the relevant candidates for the patch-file destination, independently of the original provider of its target object. No actual patch write, general source-pack attribution or final ambiguity policy is established by this lookup.

## Scene selection and tested map context

A follow-up inspection confirmed the branch order in the installed `LoadMap` coroutine:

1. The exact map identifier `rr/bushnell-whittier` selects `SceneDescriptor.BushnellWhittier`.
2. Another resolved map definition activates its data context. If its resolved map-layer identifiers contain `rr/bushnell-whittier`, it also selects `BushnellWhittier`.
3. Otherwise the loader selects the built-in `DataMap` scene if it is present in the build. If absent, it warns and falls back to BushnellWhittier with baked content.

`SceneDescriptor` contains fixed build-scene paths including `BushnellWhittier-MapManager`, `DataMap-MapManager`, and shared scenes such as `MapCore` and `GameUI`. The inspected `MapDefinition` declares `MapData`, `TerrainTileSets`, `MapLayers` and `Kind`, with no arbitrary scene-reference field. This native selection route does not take an asset-pack-supplied Unity scene path. Arbitrary DLL-driven Unity scene loading is a different, unverified mechanism; this lookup establishes no general support for it and does not prove it impossible.

The retained `crankyant/scene-operations-map` and `crankyant/broken-marker-test-map` fixture definitions explicitly inherit `rr/bushnell-whittier`. The crossing detection, equipment and reroute fixture definitions likewise inherit that layer. The [operation batch](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/f4e49fc6b2471ba3aeae01945dfedeb9c4fbd60e/Research/SceneOperationBatch/README.md) uses the same scene-operations fixture. The [ownership trial](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/56dd11cd8f60a732c1796359362a8cef5da298ec/Research/OwnershipReadiness.md) directly logs `BushnellWhittier-MapManager` for the tested A/B layered configurations. These establish base-map-derived fixture coverage, not an independent custom-map/DataMap operation trial or arbitrary mod-supplied scene coverage.

The inspected source-scene selection does not establish which MapMod's patch file is relevant to an active configuration. The earlier ownership probes did not implement or test a production patch-discovery or target-resolution mechanism. The consuming decision ticket owns that contract.

## Decision boundary

The [patch-file decision ticket](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/9) holds the product's source, distribution and multi-MapMod scope boundaries. Those policies are not empirical findings of this lookup. The inspected loader explains why an external map definition inheriting the base-game map layer can use the same source scene; it does not establish general compatibility, operation support or patch applicability for every external map.
