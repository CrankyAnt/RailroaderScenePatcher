# Static observations

Inspected Railroader 0.6.2 build 26335745. `Railroader.dll` SHA-256: `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. Methods were inspected with ilspycmd 11.2.0.10050. These are internal implementation details and require rechecking for a different build.

`Game.MapEditor.MapEditorWindow.BuildDocumentRow` invokes `TerrainPaintTool.SaveEditedTiles`, then `MapEditor.Save`, in a single try block. The catch logs the exception, presents a Save Failed alert, and refreshes the editor chrome. `MapEditor.Save` calls `MarkClean` after the map write. `Track.Serialization.MapDataIO.WriteFile` writes with `File.WriteAllText`. A Harmony prefix on `MapEditor.Save` would run after the terrain write and before the map write; this is a candidate, not a demonstrated integration or a transaction.

`Core.UndoManager.RegisterUndo` stores an action. Undo and redo open a group and execute the stored actions, so actions must register their own reverse actions to populate the opposite stack. A new group clears redo and increments `ChangeCount`; undo decrements it. `MapEditor.IsDirty` compares that count with `_cleanChangeCount`, unless upgraded data is dirty.

A standalone PowerShell process loaded the installed `Core.dll`, created an `UndoManager`, registered one action, recorded `ChangeCount` as 1, called `Undo`, and registered a different action. The resulting count was 1, `CanUndo` was true and `CanRedo` was false. This confirms count collision in the manager. It does not establish an in-editor visual result, since the standalone test did not construct a `MapEditor` or edit a map.

`Game.Scripting.Modding.Managed.ModAssemblyManager.LoadOneAssembly` loads declared assemblies and reports an error if an assembly declares no `IModEntryPoint`. The probe manifest declares only the authored probe DLL; its Harmony DLL is beside it as a dependency. The runtime probe subsequently loaded the dependency and installed its prefix.
