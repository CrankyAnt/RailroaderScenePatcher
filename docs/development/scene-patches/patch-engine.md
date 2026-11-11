<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Apply fixed placement through the core engine

The core library exposes `RailroaderScenePatcher.Core.PatchEngine` over an `IScene` adapter. The engine owns document parsing, map-context matching, dependency ordering, target uniqueness checks, preflight, snapshot, mutation, and failure handling. `SceneCapabilities` assembles the scene of a load from independent capability adapters: `IndependentBoards` for independent boards, over the native `BoardScene` port, and `BridgeScene` for the Ela bridge. It reports the active Map definition identifier and its resolved map layers. An isolated native test on Railroader 0.6.2 build 26335745 verified fixed placement, origin conversion, culler and mask updates, settled terrain work and declarative restoration. The core tests use controlled scenes for failure injection; native mutation-failure rollback and retained-world multiplayer re-entry remain unproven.

## Implement the scene adapter

`IndependentBoards` in the core library implements `IScene` for the independent-board pattern. It holds the capability's rules and talks to the game only through `IBoardWorld`, which the native `BoardScene` implements with reflection on the game's members. The core tests drive `IndependentBoards` through a controlled `IBoardWorld` that models the culler, mask and terrain lifecycle.

- `MapIds` returns the selected Map definition identifier and identifiers for its resolved map layers.
- `Capability` remains `Unchecked`. `BoardScene` checks the required native types, methods, fields and events when it is constructed; missing prerequisites prevent initialization.
- `Resolve` matches the scene name, full hierarchy path and culler component, and accepts no other `structure` than `independent-board-v1`. `Refusal` names why an object does not match the pattern: a missing or disabled culler, other than exactly one enabled circle mask, an inactive parent, a height-flattening mask, or another dependency in the hierarchy (`Track.*`, another culler, mask component or culling handler, a missing script). `Preflight` throws with that reason. The board itself may be inactive through an explicit disable. The inventory uses the same `Refusal` for its problems.
- `Read` returns the complete current state: position in map coordinates, rotation, and activity. `Apply` takes an `ObjectState`; a null placement or activity leaves that part untouched. `BoardScene` converts map coordinates with `Helpers.WorldTransformer` and checks the round trip. For an active board `Apply` requests `RequestUpdateCullingPosition` and calls the circle map mask's `Rebuild` method. Enabling and disabling rely on the `OnEnable` and `OnDisable` of the culler and mask; an inactive board gets no culling or mask call.
- After every application `Apply` verifies the pose, the activity and the registrations: `BoardScene.Registration` reads the culler's culling-sphere centre and the board's mask keys in the terrain system's modifier storage. An active board must have its sphere and every mask descriptor at its position, with no key missing from storage; a disabled board must hold neither. A mismatch throws, so the engine or session restores the captured state.
- `Capture` and `Restore` handle position, rotation, parent, and active state. `Restore` reapplies the snapshot with the same verification.

### Dependent output

`IDependentOutputScene` describes native writes that follow a mutation asynchronously. `Output(target)` returns a `DependentOutput` with the completed and outstanding writes and an error when they stopped; `RetryOutput(target)` requests the outstanding writes again. `IndependentBoards` implements it for generated terrain output:

- Each mask change records the board's old and new positions. A position whose tile `IBoardWorld.TileLoaded` reports unbuilt is complete at once, because the game builds that tile later from the current masks.
- `Poll`, called every frame by the entry point, completes a position after an `OnTileBuilt` notification covering it, once terrain work is idle with no pending invalidations. A position whose tile unloads completes for the same reason. A position still outstanding 30 seconds after the last mask change sets the error. `Failures` lists the boards whose output stopped.
- `RetryOutput` invalidates the tiles of the outstanding positions through the game's public `MapManager.Invalidate(Vector2Int)` and restarts the timer.

The engine uses the output to guard retries. After a mutation fails and `Restore` verifies, the target stays guarded while its restored output is outstanding, and the guard lifts once that output completes. Output that stops turns the guard into unverified cleanup. `AuthoringSession` exposes `Output` and `RetryOutput` for the editor panel, applies the same guard to a failed edit, and `Recheck` requests stopped output again. Generated terrain output is not a Save participant: Save does not write the output generated from board masks, and the game regenerates it from the saved placement on load. Native edited terrain tiles are a separate native Save concern; the board capability does not change them.

### Patcher version per target

`IVersionedScene.MinimumPatcherVersion(target)` returns the patcher version that first supports a target, or null. `AuthoringSession.MinimumPatcherVersion` takes the highest of the document's format requirements and these values. `IndependentBoards` returns `0.3.0` for every board except the Sylva board, which patcher 0.2.0 already supported.

## Combine capabilities

`SceneCapabilities` implements `IScene` and `IInspectionScene` over a list of capability adapters. `Resolve` asks every adapter and remembers which adapter resolved each object; an object claimed by two adapters appears twice and fails as ambiguous. Every other call goes to the adapter that resolved the object. `AddUnavailable(structure, reason)` records a capability whose game functions are missing: a target naming that `structure` resolves to a placeholder whose preflight fails with "incompatible with this game build" and the reason, while unrelated modifications still apply. `Capability` is `Incompatible` only when no adapter is available. `EntryPoint` constructs each native adapter separately, so a failed bridge check leaves board placement available and the reverse. `SceneCapabilities` also implements `IDependentOutputScene` and `IVersionedScene`: `Output` and `RetryOutput` go to the adapter that resolved the object, and an adapter without dependent output reports none; `MinimumPatcherVersion` takes the highest value any adapter returns. The board inventory's problems come from the board adapter's `Refusal`, the same check its `Preflight` runs.

## Implement a bridge capability

`BridgeScene` is the core part of the bridge capability. It implements `IScene` over `IBridgeWorld`, which `BridgeWorld` implements with Unity:

- `IBridgeWorld.Resolve` matches the supported path, scene name and root culler. `BridgeWorld.SupportedPaths` holds the one retained bridge.
- `CheckPattern` refuses inactive parents, a disabled root culler, missing scripts, rigidbodies, track-owned components (`Track.*`, `AutoTrestle.*`, `Game.Mow.TrackConstructionScenery`) in the hierarchy, a hierarchy without colliders, and a construction-scenery entry that targets the root or one of its parents.
- `Observe` returns a `BridgeObservation`: the root state in map coordinates, one `ColliderState` per collider (identity, shape properties, enabled flag, activity below the root, pose relative to the root, and for box, sphere and capsule colliders whether the physics bounds are centred on the transformed shape after `Physics.SyncTransforms`), and a text description of the separate scenery. That description covers the loaded `AutoTrestle.AutoTrestle` components within 250 m of the bridge's first observed position (local transform, activity, control points, generated flag, beam-matrix digest) and the construction-scenery controllers that reference the bridge.
- `SetPose` converts map coordinates, sets the root pose, calls `RequestUpdateCullingPosition` on every active culler in the hierarchy and synchronises physics. `SetActive` switches the root only.

`BridgeScene` records the first observation of each bridge in the load as its reference. `Preflight` refuses a bridge whose collider layout differs from that reference. `Apply` enables before the pose change and disables after it, as the board does, then verifies root pose and activity, the collider layout against the reference, collider activity against the root, and separate scenery against the observation before the change. `Restore` reapplies the captured root state and returns true only when all of those match the capture. `AuthoringSession.MinimumPatcherVersion` returns at least `0.3.0` for a document with a `bridge-colliders-v1` target, also when the bridge adapter is unavailable.

The editor's **Scene patches** window reads `NativeInventory` through `IInspectionScene`. It exposes searchable observations, dependencies and detected problems for typed markers, spans, mileposts, crossings and independent boards, plus the known Ela bridge roots, which carry the `bridge-colliders-v1` structure and any `CheckPattern` problem. `IndependentBoards` implements `IInspectionScene` by delegating to the native inventory. Refresh does not apply patches. The editor inspects only the active edited MapMod's document. These additional inspection patterns do not enable mutations. See [inventory integration](scene-inventory.md) for the observation and revalidation interfaces. The same window edits the placement and activity of an inspected object that has a mutation adapter, as described under Author through a session.

Do not infer in-game rendering or culling correctness from the adapter's prerequisite checks or core tests. The isolated native fixture test described above supplies separate empirical evidence for the tested board and build. The runtime still logs that production integration is unchecked and reports `Capability.Unchecked`; prerequisite availability and fixture observations do not establish support for other builds or the complete culling range.

## Apply a document

`PackDocuments` reads documents from loaded packs in pack order. In ordinary play, discovery skips a pack that does not declare a dependency on `crankyant/scene-patcher`. In the editor, discovery filters to the active edited MapMod and reads its document whether or not the pack declares that dependency yet; the editor's Save writes the missing or insufficient dependency entry. It decodes `Data/scene-patches.json` and `Info.json` as strict UTF-8. The `PatchEngine.Apply` overload with `documentKey` distinguishes modification identities across documents during one load. Keep the key stable for each supplying MapMod document in that load.

The returned `DocumentResult.Error` reports document-level rejection, including invalid format, source-map mismatch, incompatible capability, and duplicate modification identities. Per-modification failures appear in `Outcomes`. The engine records failures and does not retry the same document modification during the load. If a mutation fails, it asks the adapter to restore the captured snapshot. If restoration cannot be confirmed, the engine rejects later operations on that resolved target for the load.

The core engine accepts JSON text. Runtime application does not rewrite the source document. `PatchDocument.Write` returns the original text for an unedited supported v1 document. After an edit it returns the canonical text, in which every number that was not replaced keeps its source literal. There is no format-conversion API in this slice. Unsupported or malformed documents are protected against writes.

## Author through a session

`AuthoringSession` owns one editor session for the active edited MapMod. It takes the `PatchEngine` that applied the document at world load, an `INativeHistory` and an `IStorage`.

- `Edit`, `Remove` and `Recheck` accept the `InspectedTarget` of the inspected observation; the editor panel always passes `inventory.Pin(selected)`, so a stale inventory or changed observation refuses the edit. Immediately before the scene mutation, with or without a token, the session runs the engine's guard: preflight again, the same unique object, the same map context, and the token when given. A rejection there restores the document and requests no scene restoration, because no mutation started. History replay passes no token, since Undo and Redo return to states the user did not inspect, and still runs the guard.
- `Edit` and `Remove` resolve and preflight the target, change the document, and reconcile the object: the engine's underlying state for that object, captured before its first mutation, overlaid with every effective modification that resolves to it. Removal therefore recomputes the state instead of restoring a snapshot. A failure restores document and object; an unverified restoration blocks the object until `Recheck` verifies it.
- Each completed change registers one inverse with the native history. The inverse sets the modification to an absolute earlier state and registers its own inverse, so Undo and Redo are repeatable. It never throws: an exception inside `Core.UndoManager.Undo` leaves the native grouping open. A failed replay keeps the document, sets `IncompleteOperation` and keeps the session dirty until a complete Save. When native Undo or Redo itself throws, the native manager keeps its partial group open and later edits reach neither its stack nor its change count; the session then keeps native map data outstanding and `IncompleteOperation` set through every Save until the editor is reloaded.
- `Outstanding` names the participants that still need a write: native map data, patch document and dependency metadata. The patch document is outstanding when its canonical text differs from the last text read or written. Native data is outstanding when the editor's own count says so, or when the history identity differs from the identity recorded at the last complete Save.
- The history identity gives every undo group a token. Undo and Redo carry the token to the group the native manager creates, a new edit gets a new token, and when the manager prunes its oldest group at 50 levels the state below the stack takes the pruned group's token. Two histories with the same native change count therefore differ, and a pruned or cleared history is never mistaken for the saved state.
- `BeginSave` writes the dependency metadata and then the patch document, each only when needed, and throws on the first failure. `CompleteNativeSave` records the saved identity. `FailSave` reports completed and outstanding writes. The patch write first compares the file with the text last read or written and refuses to overwrite an external change.

`Hooks` connects the session to the editor with Harmony patches, installed once per process and checked as one capability: a prefix, postfix and finalizer on `Game.MapEditor.MapEditor.Save`, a postfix on its `IsDirty` getter, a prefix and finalizer on `Core.UndoManager.Undo` and `Redo`, and a prefix on `MapEditorWindow.ConfirmExitEditor`. The Save prefix runs after the editor wrote edited terrain tiles and before the native map write. After Undo and Redo the finalizer settles the history identity and calls `MapEditorWindow.RefreshChrome`, because the native handler rebuilds the title and Save button before that point. When a required member is missing, authoring is disabled with a message and runtime application continues. These hooks were exercised in recorded isolated journeys on Railroader 0.6.2 build 26335745: native authoring journeys A and B on commit `caa9d3e`, and UI replay C on commit `9fb3ce4`. Those journeys establish only the cases they measured on that build and fixture; they do not establish other builds, object families, history pruning, an exception inside native history execution or multiplayer.

## Build and run the core test

Build the mod project against the local Railroader managed assemblies:

```powershell
dotnet build Src/RailroaderScenePatcher -p:GameManaged="C:/path/to/Railroader/Railroader_Data/Managed"
```

Build and run the core test project with the installed .NET SDK:

```powershell
dotnet run --project Tests/RailroaderScenePatcher.Core.Tests --configuration Release
```

The core test uses an in-memory adapter, a model of the native undo manager's grouping, pruning and change count, and storage with a failure switch per write. It does not load Railroader, exercise a native scene object, or prove native history, title or Save button behavior.

Build outputs and intermediate files go to `RailroaderScenePatcherBuild` under the operating system's temporary directory. Override `BuildArtifactsRoot` with an absolute path when separate builds need independent output directories. The core tests restore Newtonsoft.Json 13.0.2 and need no game assemblies. The native project needs the local game DLLs through `GameManaged`; it does not distribute those files. It restores Lib.Harmony 2.4.2 and copies `0Harmony.dll` to its output.

For an isolated load test, copy only `RailroaderScenePatcher.dll`, `RailroaderScenePatcher.Core.dll`, `0Harmony.dll` and `Info.json` from the native build output into the selected test pack. The game already supplies Newtonsoft.Json. Replacing loaded DLLs requires a process restart. This is local test preparation, not a release package.
