<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Apply fixed placement through the core engine

The core library exposes `RailroaderScenePatcher.Core.PatchEngine` over an `IScene` adapter. The engine owns document parsing, map-context matching, dependency ordering, target uniqueness checks, preflight, snapshot, mutation, and failure handling. `BoardScene` supplies the first native adapter, for the independent Sylva board in `BushnellWhittier-MapManager`. It reports the active Map definition identifier and its resolved map layers. An isolated native test on Railroader 0.6.2 build 26335745 verified fixed placement, origin conversion, culler and mask updates, settled terrain work and declarative restoration. The core tests use controlled scenes for failure injection; native mutation-failure rollback and retained-world multiplayer re-entry remain unproven.

## Implement the scene adapter

The current `BoardScene` implements `IScene` for this target pattern:

- `MapIds` returns the selected Map definition identifier and identifiers for its resolved map layers.
- `Capability` remains `Unchecked` when required native types, methods, and events are available. Missing prerequisites prevent initialization.
- `Resolve` matches the scene name, full hierarchy path, and culler component. `Preflight` requires the exact supported path, active board/culler/circle mask, and no unexpected `Track.*` child. Consecutive valid moves retain every intermediate location in the pending terrain coverage checks.
- `Capture`, `Apply`, and `Restore` handle position, rotation, parent, and active state. `Apply` converts map coordinates with `Helpers.WorldTransformer`, checks the converted pose, requests `RequestUpdateCullingPosition`, and calls the circle map mask's `Rebuild` method.
- `PollCompletion` waits for `OnTileBuilt` notifications at the old and new board positions and idle terrain work with no pending invalidations before it reports the dependent terrain state complete.

The editor's **Scene patches** window can inspect supported board identities, focus the selected board, refresh stored identity matches and prerequisites, and copy the generated identity JSON. Inventory refresh does not apply patches. The editor processes only the active edited MapMod's document. Other object families, track binding, activity changes, authoring controls, and Save/Undo are not implemented.

Do not infer in-game rendering or culling correctness from the adapter's prerequisite checks or core tests. The isolated native fixture test described above supplies separate empirical evidence for the tested board and build. The runtime still logs that production integration is unchecked and reports `Capability.Unchecked`; prerequisite availability and fixture observations do not establish support for other builds or the complete culling range.

## Apply a document

`PackDocuments` reads documents from loaded packs in pack order. Each pack must declare a dependency on `crankyant/scene-patcher`. In the editor, discovery filters to the active edited MapMod. It decodes `Data/scene-patches.json` and `Info.json` as strict UTF-8. The `PatchEngine.Apply` overload with `documentKey` distinguishes modification identities across documents during one load. Keep the key stable for each supplying MapMod document in that load.

The returned `DocumentResult.Error` reports document-level rejection, including invalid format, source-map mismatch, incompatible capability, and duplicate modification identities. Per-modification failures appear in `Outcomes`. The engine records failures and does not retry the same document modification during the load. If a mutation fails, it asks the adapter to restore the captured snapshot. If restoration cannot be confirmed, the engine rejects later operations on that resolved target for the load.

The core engine accepts JSON text. Runtime application does not rewrite the source document. `PatchDocument.Write` returns the original text only when the document was parsed as a supported v1 object. This also preserves unknown fractional values without numeric rounding. There is no document editing or format-conversion API in this slice. Unsupported or malformed documents are protected against writes.

## Build and run the core test

Build the mod project against the local Railroader managed assemblies:

```powershell
dotnet build Src/RailroaderScenePatcher -p:GameManaged="C:/path/to/Railroader/Railroader_Data/Managed"
```

Build and run the core test project with the installed .NET SDK:

```powershell
dotnet run --project Tests/RailroaderScenePatcher.Core.Tests --configuration Release
```

The core test uses an in-memory adapter. It does not load Railroader, exercise a native scene object, or prove that the mod entry point applies documents in game.

Build outputs and intermediate files go to `RailroaderScenePatcherBuild` under the operating system's temporary directory. Override `BuildArtifactsRoot` with an absolute path when separate builds need independent output directories. The core tests restore Newtonsoft.Json 13.0.2 and need no game assemblies. The native project needs the local game DLLs through `GameManaged`; it does not distribute those files.

For an isolated load test, copy only `RailroaderScenePatcher.dll`, `RailroaderScenePatcher.Core.dll` and `Info.json` from the native build output into the selected test pack. The game already supplies Newtonsoft.Json. Replacing loaded DLLs requires a process restart. This is local test preparation, not a release package.
