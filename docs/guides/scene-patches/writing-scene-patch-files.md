<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Write a scene patch file

You can use a text editor to create or correct the file that **Scene Patcher** reads and writes. Keep one `Data/scene-patches.json` in the supplying mod. Check your changes with a copy of the mod and a disposable save.

## Create the file

1. Open the map in the native map editor, inspect each target in **Scene Patcher**, and click **Copy ID**. Use the scene target map and exact target members the editor supplies, including `padMarkerId` for a crossbuck with a shared path. See [Inspect scene objects](inspect-scene-objects.md).
2. Back up an existing file before editing it. For a new file, start with the [complete v1 example](scene-patch-file-v1.md#minimal-example). Save as strict UTF-8 JSON. Set `sourceMap.mapId` to the map you want to target. Maps that inherit it also receive the patch. If you're editing a map patch, target one of its map layers, such as the bottom map layer.
3. Give each modification a unique key in `modifications`. Use the copied `target` and add only the settings you want to change. Use `dependsOn` for prerequisite modifications in that file, or `[]` when there are none. See the [v1 reference](scene-patch-file-v1.md#modification-fields) for placement, activity, and supported relationships, including when an explicit detach is valid.
4. Check the [dependency minimum](scene-patch-file-v1.md#dependency-minimum) for every modification. In `Info.json`, include `crankyant/scene-patcher >=<minimum>` in `dependencies.required`, using a version sufficient for the whole file. Keep stricter compatible constraints.

For a worked placement, see [Write fixed scene placement](writing-fixed-placement.md). For crossing edits, use the [operation guides in the manual](modder-manual.md#choose-an-operation) to check the circuit, source, and controller prerequisites.

## Validate the result

1. Check that the JSON has no repeated property names, uses the correct format version, and has complete target identities. Check dependency IDs for missing entries and cycles. Preserve unknown members and modifications when correcting an unrelated setting.
2. Open the edited mod in the native map editor and inspect the targets and their **Changes** tabs. If you changed the file during an open session, use the [version choice](editing-placement-in-the-map-editor.md#choose-a-scene-patch-file-version). Review which modifications applied and which were refused before using native **Save**.
3. Load the target map with the test copy of your mod and the dependency enabled. Check the intended changes and their dependent state, such as a regenerated pad, circuit detection, or collision at a moved object. Read the game warnings for refusals. Check that unrelated objects and relationships still work.

## Correct a problem

- If a target is missing or ambiguous, confirm the intended object again and replace the identity with the value from **Copy ID**. Check that the scene target map is in the loaded map context.
- If an independent modification fails, read the reason and correct the target, value, or prerequisite it names. Dependent modifications can't apply while it fails. Test the corrected file on the next world load.
- If the patcher protects the file from overwrite, follow [Recover a protected scene patch file](recover-scene-patch-files.md). Use **Read again** when that action is available. Keep the backup until you've checked the corrected file. Use a compatible patcher version to edit content written in a newer format.
