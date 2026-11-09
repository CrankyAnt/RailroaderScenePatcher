# Native Save and Undo probe protocol

This is an experimental probe for issue #34, not a supported integration. It targets the inspected 0.6.2 build. Build against the installed managed assemblies and Harmony 2.4.2. The fixture is disposable; never run it on a personal map.

The probe attaches a Harmony prefix to `MapEditor.Save`. The prefix writes a small authored-state file, or throws a controlled `IOException` when toggled. The native Save button catches exceptions around terrain saving and `MapEditor.Save`. The prefix does not cover terrain saving and does not supply rollback or atomicity. A native-map lock forces a native write error only if the operating system rejects the game's file access; observe the result rather than assume it. Release the lock immediately after the test.

Apply registers an inverse action on the editor's `UndoManager`. The action changes the positions of two temporary Unity objects, and the inverse registers the reverse action during undo and redo. Record the state, dirty flag, count and undo/redo names before and after each operation. Surround it with ordinary native editor edits. The objects do not represent the full scene-patch data model and the file format is a fixed-value probe, not a product serializer.

First establish a successful native Save with the probe enabled. Observe custom and native files, the dirty flag and the existing Save button. Apply another edit, enable the custom failure, invoke the native Save and capture the dialog, file contents and dirty state. Disable the failure and retry. Next, apply an edit, lock the disposable native map, save, capture the custom write, native error and dirty state, release the lock and retry. Never infer success merely from the absence of a dialog.

To examine the counter collision, save an edit, undo it and make a different native edit. Compare the actual map state with `IsDirty` and `ChangeCount`. A standalone invocation of the installed `Core.UndoManager` already showed saved count 1 and current count 1 after this sequence, with redo cleared. That observation alone does not prove the editor UI result.

Record the exact game build and hashes, probe source revision, map and log evidence. Close the game, remove only the test packs, and restore the original test-profile activation state. Do not start the game until the user explicitly approves the launch.
