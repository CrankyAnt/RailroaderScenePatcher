# Native Save and Undo feasibility result

## Conclusion

Railroader 0.6.2 build 26335745 provides a demonstrated native integration route for a minimal custom two-object operation and coordinated custom/native Save. Native history executes Undo and Redo. Additional saved-state identity is needed because the native change counter alone can alias different histories. The tested extension must refresh native editor chrome after transferring history identity; otherwise its corrected dirty getter and the existing UI can disagree.

This is bounded feasibility evidence, not a production implementation or proof of arbitrary document handling. It does not require a separate Save command or replacement history manager. Full document persistence, history pruning, exceptions during history execution, other hooks, terrain-write failures and supported-version checks remain implementation and validation responsibilities.

## Retained trials

- [Original observations](NativeSaveUndoProbe/RuntimeObservations.md) demonstrate interleaved native/custom history, Save hooks and the false-clean change-count collision. Their transform and duplicate-hook limitations remain explicit.
- [V2 observations](NativeSaveUndoProbeV2/RuntimeObservations.md) independently read both Unity transforms, verify one Save prefix, preserve dirty state on the tested divergent-history branch, and exercise custom-write and native-write failure/retry. They also identify the stale title after Redo. Executed source is commit `438dd6b919d1a1533b8cebab6462ac8bb03dc296`; DLL SHA-256 is `FF12458B764D7226EEEA35B11800B6C8F2D2A45A86EE5F7964435705DFBF503B`.
- [V3 source](NativeSaveUndoProbeV3/Probe.cs) attempted chrome inspection but cast `UI.Builder.UIPanel` to `UnityEngine.Component`. The cast failed. Its [raw log](NativeSaveUndoProbeV3/ProbeV3.log) therefore does not establish title or button state at the requested phases. A visible clean title after refresh was insufficient to prove interactability.
- [V4 source](NativeSaveUndoProbeV4/Probe.cs) corrects the inspection to the panel's `_container` component, instruments dirty-getter calls during Redo, and preserves the same native history actions and identity-transfer scheme. Only the unresolved Redo sequence was repeated, with the minimum apply/Save/Undo setup. Earlier valid results were not repeated.

## Redo diagnosis and correction

The [V4 raw log](NativeSaveUndoProbeV4/ProbeV4.log) records the following sequence after returning to the saved custom state. The inspector finds the Save label within the native chrome container and reads both `Button.interactable` and `Button.IsInteractable()`.

| Phase | Dirty getter | Native title star | Save interactable |
| --- | --- | --- | --- |
| Native getter calls during Redo | native false, augmented true | UI rebuild occurs on this path | UI rebuild occurs on this path |
| Before identity transfer | true | present | true through both button checks |
| After identity transfer | false | present | true through both button checks |
| Immediately after `RefreshChrome` | false | absent | two transient Save components, no single-button assertion |
| Following frame | false | absent | false through both button checks; exactly one active Save button |

Redo restored A to `(1, 0, 0)` and B to `(2, 1, 0)`. No Save ran after Redo. The correction changes UI presentation after identity accounting; it does not persist data to make the state appear clean. The immediate duplicate controls are explicitly recorded rather than selecting an arbitrary instance. The following-frame observation establishes the settled state.

The installed native implementation explains the order. `Core.UndoManager.Redo` calls `EndUndoGrouping` before returning. That method pushes the newly generated Undo group and synchronously invokes `DidChange`. `Game.MapEditor.MapEditorController.OnUndoManagerChanged` rebuilds the window during Undo/Redo. `MapEditorWindow.ComputeTitle` and `BuildDocumentRow` query `MapEditor.IsDirty`; `RefreshChrome` recomputes the title and rebuilds its chrome panel. The probe's Harmony postfix transfers the consumed group's identity only after native Redo returns. V4 directly records the augmented getter returning true during native Redo and false after transfer. The stale title and enabled button are therefore explained by this probe's late identity update relative to the native UI notification.

Inspected methods are in the installed `Core.dll` and `Railroader.dll`, inspected read-only with Windows SDK ildasm. Installed `Railroader.dll` SHA-256 is `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. No vendor assembly or disassembly is included in the research branch.

## Execution identity and limits

V4 source SHA-256 is `E03659F4CE6312255C6F02788004C8A9D922509098D2B5AD6F52DB08C72B2DDC`. The built and executed DLL SHA-256 is `B903D034FCDABE766BF5102B2E2C63BF2B1DF9430F34E71289654D6DFFA40A7E`. The build targets net472 and uses the installed managed game assemblies and Harmony 2.4.2. It completed with zero errors and zero warnings. Fixture and manifest are retained beside the source. The isolated profile loaded only this probe and its disposable layered map.

V4 demonstrates one saved custom-state return, not every Undo/Redo sequence. Refreshing chrome after identity updates is a demonstrated repair for this case; applying that ordering consistently across the finished authoring implementation still needs its own validation. Comparing retained history identities can conservatively report dirty after pruning or semantically equivalent histories. A production implementation must not claim counter equality alone establishes saved state.

The coordinated-write evidence remains that of V2. Custom-write failure leaves work dirty and reports Save Failed. Native-write failure can follow a successful custom write; it still reports failure and keeps outstanding work dirty until retry succeeds. This is truthful partial-success handling for the tested points, not multi-file atomicity or crash recovery.
