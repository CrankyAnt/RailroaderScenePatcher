# Native editor findings

This lookup supports [issue #8](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/8). It used read-only IL inspection without launching the game. The inspected installed assemblies were `Railroader_Data/Managed/Railroader.dll`, SHA-256 `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`, and `Core.dll`, SHA-256 `ED9092B9A5918864FB35634544731303E53FE553C34F9FB84E98E2E514052EB9`. These findings describe the inspected build, not a supported extension API or a working custom integration. The official [map guide](https://docs.railroadergame.com/modding/map-modding/) describes native editing and reopening a map.

## Editing, save and history

`GraphEditor.SetNodePosition` records an undo action and updates the live node. `ObjectsEditor.SetPosition` updates the map-object DTO and its preview, then registers undo. `ObjectsEditor.RegisterTransform` commits a transform preview and registers undo. These examples show native changes taking effect before Save; they do not establish the behavior of every tool or custom scene edits.

The Save callback `MapEditorWindow/<>c__DisplayClass58_0.<BuildDocumentRow>b__1` calls `TerrainPaintTool.SaveEditedTiles` and then `MapEditor.Save`. `MapEditor.Save` serializes the map DTO or writes the composition layer delta and calls `MarkClean` after writing. `IsDirty` compares the undo manager change count with the clean count and also reports upgraded data as dirty. These paths do not prove atomic terrain and map writes, or persistence of arbitrary scene state. A custom write must coordinate its own failure handling and dirty state; the declared `MapEditor` events did not expose a named save event.

`MapEditor.Undo` and `Redo` delegate to `Core.UndoManager`. `RegisterUndo` adds actions to groups, and new groups clear redo. `PerformUndoable` groups an action. A custom edit would need correct inverse actions and dependent state; access to these methods does not prove successful integration.

`BuiltinPlacementTool.Arm` and `SpanEndpointTool` support cancelling a pending pick. Cancel clears that pending interaction, whereas undo reverses a completed registered edit. `MapEditorWindow.ConfirmExitEditor` presents Cancel and Exit, adding an unsaved-changes warning when `IsDirty` is true. The map guide says exiting discards unsaved edits. The inspected paths do not establish rollback of arbitrary custom mutations.

## Spatial selection and transforms

`OpsInspector.BuildSpanEndpointField` arms `SpanEndpointTool` for a native span endpoint. `TrackClickPicker` raycasts track and returns one resolved `Track.Location`; a valid pick commits and disarms, an invalid pick does not commit, and right-click or a change of native selection disarms. `BuiltinAnchorTool` follows a similar target and pick pattern. Neither establishes an ambiguity-resolution workflow, custom scene selection, or a node-picking workflow.

`StrategyCameraController.UpdateInput` reads camera movement independently of the armed tools. The tools cancel on right-button press, which also starts camera rotation. The inspected methods do not establish execution order or that a pending choice survives right-drag. Other input routing remains a runtime question. `MapEditorController.HandleEscapeCancel` and `CancelFirstArmedInteraction` disarm the tools.

`MapEditorTransformGizmo.Initialize` creates a lower-level `UI.Gizmos.TransformGizmo` and subscribes to drag events. The native wrapper obtains `IMapEditorTransformable` instances from specific native selection types. The lower-level component and event pattern are an adapter candidate, not proof that arbitrary retained scene objects can use the native selection or safely update dependent state.

## Reload and camera

`EditorMenuWindow.EditMap` calls `StateManager.RelaunchEditorWithMap`, whose inspected path returns to the main menu and launches an editor scene with the selected map. The documented Edit Map reopen route does not imply a full operating-system process restart. The new editor session constructs a new `MapEditor` and `UndoManager`, so that construction path does not carry the prior session's undo history. The relaunch method itself does not establish that a programmatic relaunch presents an unsaved warning or preserves custom work.

`CameraSelector.InitializeCameras` calls `PlayerCameraPersistence.TryRestore`, and `CameraSelector.JumpToPoint` accepts a position and rotation. These paths do not prove restoration of the exact previous editor camera pose, zoom or target. Camera continuity requires a runtime check.
