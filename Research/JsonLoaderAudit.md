# Railroader 0.6.2 JSON loader audit

## Finding

The installed managed code exposes native JSON routes for map data and map-object DTOs. It does not expose a JSON field or registered DTO for `Track.TrackMarker`, nor a scene-patch path in the map-data loader chain inspected here. Therefore the requested marker retargeting or deactivation is not supported by the native map JSON mechanism found in this audit. This is a bounded finding about the installed `Railroader.dll` and inspected asset-pack files, not proof that no external or undocumented mechanism exists.

## Loader chain

`Game.MapLoader.LoadMapData` resolves the map-data path, constructs `Track.MapDataLoader`, and passes `Game.MapLoader.MutateMapJson` as its JSON mutator for normal non-edit-target loading. `MutateMapJson` calls `Game.Scripting.Modding.MapDataScriptManager.ExecuteOnMapData` after data scripts load.

`Track.MapDataLoader.Load` calls `Track.Serialization.MapDataIO.LoadComposed`, then immediately deserializes the result through `Track.Serialization.MapSerializer.Deserialize`. It returns graph, ops, map-object DTOs, and spawn DTOs. It does not receive a scene or scene-component collection.

`MapDataIO.LoadComposed` parses JSON from the primary path and map-layer paths, composes the JSON objects, applies the JSON mutator, and calls `FromJObject` to obtain `MapDTO`. `MapDTO` has `graph`, `ops`, `objects`, `spawns`, and `folders`; it has no scene or marker collection.

Map composition is a real native JSON extension point. `MapDefinition` has `MapData` and `MapLayers` asset references. `MapDataScriptManager.ExecuteOnMapData` finds each pack's `Data.rl`, builds a pack-scoped script instance, and calls its `modifyMap` handler with a `JsonDataContext`. That context's merge operation applies a JSON Merge Patch to the in-memory map JSON before `MapDTO` deserialization. These routes can edit fields in the map JSON schema; the inspected code does not connect them to Unity scene objects.

## Marker and map object distinction

`Track.TrackMarker` is a `MonoBehaviour` with `id`, `type`, and a serialized track `Location`. Its `OnEnable` checks whether the location is valid, logs a warning if invalid, and otherwise calls `Track.Graph.AddNamedLocation(id, location, type, marker)`. `OnDisable` removes the named location. This lifecycle runs on Unity scene components and is separate from the map DTO load shown above.

By contrast, `Track.Data.MapObjectDTO` has only `Kind`, `Position`, `Rotation`, `Scale`, `Folder`, and nullable `Enabled`. `MapObjectConverter.ReadJson` chooses a registered map-object DTO subclass from the `kind` field. `MapObjectManager.SpawnAll` conditionally spawns those DTOs, and `MapObjectSpawner.SpawnOne` calls the registered builder. This is a JSON-authored object spawning path, but it does not turn a DTO into a `TrackMarker` or identify an existing scene component by marker ID.

`Data/Map.json` in the installed base pack contains graph data. The inspected `Definitions.json` registers the map using `mapData: "./Map.json"` and terrain tile sets. It does not declare a scene path. `MapDefinition` metadata in the managed assembly likewise has no scene field.

## Assessment for the three old markers

The three IDs in scope (`M5c0`, `M5ib`, `Muef`) belong to the scene-marker mechanism described above, while the requested replacement anchors (`Ssn915zyw`, `Ssn914m3o`, `S43k2x9ts`) belong to the graph/map data. A map JSON patch can change graph nodes/segments or DTO-backed map objects, but no inspected native JSON mechanism can retarget or disable an already-authored `TrackMarker` component. `MapObjectDTO.Enabled` only controls DTO-backed map objects and does not provide an override for scene components.

Restoring the old segments would only make the old marker locations valid against obsolete track, which does not repair the intended alignment. The provided context also says the `Sqc5` replacement moved the endpoint substantially, so old-segment restoration would reintroduce unwanted rails and geometry.

## Evidence and limits

Evidence comes from metadata and IL in `Railroader_Data/Managed/Railroader.dll` (game version identified by the assignment as 0.6.2), plus the installed `AssetPacksCore/nc-bryson-terrain/Definitions.json` and `Data/Map.json`. The main loader methods inspected were `Game.MapLoader.LoadMapData`, `Game.MapLoader.MutateMapJson`, `Track.MapDataLoader.Load`, `Track.Serialization.MapDataIO.LoadComposed`, `Game.Scripting.Modding.MapDataScriptManager.ExecuteOnMapData`, `Game.Scripting.Modding.JsonDataContext.Merge`, `Track.TrackMarker.OnEnable`, `Game.MapObjectManager.SpawnAll`, and `Game.MapObjectSpawner.SpawnOne`.

This audit did not inspect every native Unity scene asset serialization format or every code path in all managed assemblies. The conclusion is specifically that the supported map JSON path and DTO registry inspected here do not provide the requested existing-scene-marker override. A conclusive follow-up on actual base-map marker ownership should inspect the resolved scene asset and any scene-instantiation code that operates outside `MapDataLoader`; that follow-up is unlikely to create a JSON override unless it reveals a separate scene-patch API.

## Map-object edge cases checked

Map-object IDs do not match or replace existing Unity scene objects. `MapObjectManager` constructs a new root named `Map Objects`; `MapObjectSpawner.CreateRoot` creates that root and moves it into the target scene. `SpawnAll` iterates only the dictionary of map-object DTOs and calls `SpawnOne` for each eligible entry. `SpawnOne` creates a new inactive `GameObject` named from the DTO key under that new root, then calls a registered builder. The manager's `TryGetInstance(id)` searches only direct children of its own `_root` by GameObject name. It does not search the scene or find an existing `TrackMarker`. A DTO entry keyed `M5c0` would at most spawn a separate object called `M5c0` if an appropriate DTO kind and builder were registered; it would not retarget or replace the scene marker.

`MapObjectDTO.Enabled` is nullable. `MapFolders.ResolveEffectiveEnabled` resolves DTO and folder enablement; `MapObjectManager.SpawnAll` skips disabled DTOs before `SpawnOne`. `MapObjectActivation` applies activation to spawned DTO objects. This controls the map-object population created under the manager's root. `TrackMarker.OnDisable` is a Unity lifecycle callback on the existing marker component, and the inspected DTO manager has no reference or operation that invokes it on a scene-authored marker. Setting a map-object DTO's `enabled` to false therefore cannot disable the old marker.

`MapObjectDTO` does have a Newtonsoft `JsonExtensionData` dictionary (`_extraFields`), so unknown properties are retained on a DTO. The inspected `MapObjectConverter.ReadJson` chooses the concrete DTO type only from the `kind` field via `MapObjectRegistry.ResolveDtoType`; it removes `kind` before deserializing registered kinds. The registry maps kinds and aliases to DTO types, and maps DTO types to builders. Unknown kinds resolve to `UnknownMapObjectDTO`, which has no builder path shown. `TrackDataJson.CreateSettings` registers the map-object converter but does not set `TypeNameHandling`; it constructs default `JsonSerializerSettings`. Extension data changes JSON preservation, not runtime scene lookup or marker lifecycle. A separate registered mod DTO/builder could assign custom semantics to its own fields, but that would still be a mod-created object path and the inspected manager does not target pre-existing scene objects.

`MapDefinition` has no scene-path property in the inspected managed type, and the installed base map definition points at map data and terrain tiles only. No JSON scene override was found in these paths.

## Base map scene-selection path

The base map's scene does not come from `Map.json`. `UI.Menu.GlobalGameManager/<LoadMap>d__62.MoveNext` special-cases identifier `rr/bushnell-whittier`, clears the active map-definition data path for the built-in case, and selects the compiled-in `SceneDescriptor.BushnellWhittier`. `SceneDescriptor` is initialized in managed code with `Assets/Scenes/BushnellWhittier-MapManager.unity`. The loader then builds a scene list containing `MapCore`, the selected map scene, and `EnvironmentEnviro`, and loads each through Unity `SceneDescriptor.LoadAsync`.

For other registered map definitions, the same state machine may select the compiled-in `DataMap` scene descriptor if `Assets/Scenes/DataMap-MapManager.unity` exists in the build. It does not take a scene path from the map definition. `MapDefinition` metadata lists only `MapData`, `TerrainTileSets`, `MapLayers`, and `Attribution`. `MapLayerResolver.Discover` follows `MapLayers` by resolving each identifier to another registered `MapDefinition`; those definitions contribute map-data layer paths, not scene instances. The inspected data path therefore has no JSON-selected Unity scene or JSON scene-patch registration point.

The `JsonDataContext` native script API exposes `modify`, `merge`, and `asString`, all on the in-memory JSON context. `merge` applies JSON Merge Patch. The API does not expose a Unity scene, `GameObject`, `TrackMarker`, or component handle. Arbitrary executable `Data.rl` could contain other scripting operations available to that pack, but that is code, not JSON-only authoring.

`MapObjectDTO._extraFields` is marked `JsonExtensionData`, so arbitrary JSON properties can survive round trips. `TrackDataJson.CreateSettings` creates default `JsonSerializerSettings` and sets formatting, contract resolution, and its converters. It does not set `TypeNameHandling` or `MissingMemberHandling`. The map-object converter resolves types only through `kind` and `MapObjectRegistry`; it does not instantiate types named by arbitrary JSON CLR type metadata. Registered custom DTOs/builders can define mod-specific object behavior, but `MapObjectSpawner` still creates new objects under its own `Map Objects` root, and `MapObjectManager.TryGetInstance` only checks that root's direct children. These mechanisms do not address the pre-existing scene-authored `TrackMarker` IDs.


The `TrackDataJson.CreateSettings` IL contains no calls to `set_TypeNameHandling` or `set_MissingMemberHandling`. `MapObjectManager.TryGetInstance` IL only enumerates the manager root's children and compares their GameObject names with the requested key; it has no scene-wide lookup or component query.

The built-in `MapObjectRegistry` static initializer registers only `Prop`, `CircleMask`, `RectangleMask`, `CurveMask`, and `Builtin` kinds, plus the `Scenery` alias for `Prop`. It registers no `TrackMarker` or scene-mutation kind. Mods may register custom DTO kinds and builders through code, but that extends the map-object spawn mechanism described above.
