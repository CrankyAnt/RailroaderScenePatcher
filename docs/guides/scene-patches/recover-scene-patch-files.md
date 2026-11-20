<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Recover a protected scene patch file

Use this guide when **Scene Patcher** reports that the edited mod's `Data/scene-patches.json` is protected from overwrite. The patcher protects a scene patch file if it cannot read it, its format version is missing, unrecognized, or newer, it names another map, or it contains numbers whose written form cannot be preserved exactly. The native map editor's Save will not overwrite a protected scene patch file. If the mod's `Info.json` cannot be read, editing is also unavailable. Correct `Info.json` and click **Read again**. **Start fresh** is unavailable because the problem is in `Info.json`.

If a readable scene patch file changes outside the editing session, you can click **Use file version**, **Keep editor version**, or **Import** in the **Version choice** panel. If it is malformed, unreadable, or has an unsupported version, **Scene Patcher** shows **File needs repair** in the **Repair** panel. Before **Keep editor version** allows Save to overwrite that file, the patcher must create and verify a backup.

If the scene patch file was deleted outside the editing session, **Keep editor version** needs no backup. The next Save writes the editor version.

## Read what is wrong

1. Open **Scene Patcher** in the native map editor, then click the page icon to open its session details.
2. Read the reason for protection in the **Scene patch file** section. Each problem lists:
   - the JSON path, such as `sourceMap.mapId` or `modifications.place-board`;
   - the line or lines in the file, or **none** when a field is missing;
   - **Text there**: up to eight lines and 1,200 characters from that place, followed by the number of omitted lines if the passage is longer. For a file that can't be read, a passage that reaches the end of the file ends with `(The file ends here.)`, so an open quote or bracket there belongs to the file;
   - **Expected**: the structure or value the format expects.
3. Click **Format reference** to open the [scene patch file reference](scene-patch-file-v1.md) or **Recovery guide** to open this guide.

For a readable scene patch file, the section also lists modifications the patcher cannot apply, such as content written by a newer patcher version. It preserves those modifications unchanged.

## Correct the file and read it again

1. If you edited the map in this session, save your native map work and reopen the native map editor. Recovery is unavailable if native map work is unsaved or the session's Undo history has changed. This prevents recovery from replacing history.
2. Correct `Data/scene-patches.json` in a text editor. If an object is missing or its identity matches several objects, check the intended object's [inventory identity](inspect-scene-objects.md). The patcher does not guess which object you mean or substitute a nearby one. If you need content from a newer format version, use the patcher version that wrote it. Keep content you do not understand unless you intend to remove it.
3. Click **Read again** and read the review. Click **Read again** to confirm or **Cancel** to leave everything unchanged.

If the patcher can read the corrected scene patch file, the session adopts it and applies its modifications immediately. The result reports how many modifications applied and names any that did not. Reading the scene patch file does not write it. You can then edit and save it. Each edit changes only the field you edit and preserves the other values. The patcher also rereads the inventory and the supplied definitions. If the scene patch file still has a problem, it remains protected and the diagnostics show the current problems.

## Start fresh

Start fresh only if you intend to remove everything in the scene patch file, including content this patcher does not understand.

1. Save your native map work and reopen the native map editor if you edited the map in this session.
2. Click **Start fresh** and read the review. Click **Back up and start fresh** to confirm or **Cancel** to leave everything unchanged.
3. The patcher makes a byte-for-byte copy of the original beside it, named `scene-patches.json.backup-<id>`, and verifies the copy before proceeding. This works even if the original is not valid text. If the copy fails, the scene patch file remains protected. The backup path appears in the **Scene patch file** section and the result message.
4. Click the native map editor's **Save**. It replaces the original with an empty scene patch file in format version 1, naming your map's bottom map layer. The patcher keeps the backup and never deletes or restores it.

If the original or the backup changes after **Back up and start fresh**, Save is refused and shows the reason. It writes nothing. Both files remain, and your map and terrain work stays unsaved. Reopen the native map editor to read the scene patch file again.

## Keep content you do not understand

Every read, edit, and Save preserves unknown fields at the scene patch file root and inside known objects, along with modifications the patcher cannot apply. You cannot edit or remove a modification with such content in the native map editor. Correct or remove it in the scene patch file, then read the scene patch file again.

Save adds or raises the patcher dependency in `Info.json` only if the scene patch file requires it. It preserves a stricter minimum and every other constraint, and adds no upper bound. If a constraint excludes the required version, Save reports it without changing the constraint.

## Keep the editor version while the file needs repair

Click **Keep editor version** if the scene patch file needs repair and you want to replace it with the scene patch file loaded in this session, including your edits. The review shows the relative path where the patcher will back up the current file before overwriting it. Confirm only if the backup should be kept with the mod.

The patcher writes a byte-for-byte copy beside the changed file and verifies it before Save can overwrite the original. If it cannot write or verify the backup, the original remains protected. The review shows a new backup path before you retry. After a successful backup, the diagnosis stays visible and its line numbers refer to the backup. To copy the full diagnosis, click **Copy** in the **Repair** panel before the backup, or **Copy to clipboard** in the session details afterward. Later checks do not return the scene patch file to **File needs repair** unless its content changes again. If a check finds that the file is readable but still has outside changes, **Scene Patcher** says `The file is readable again.` and offers the normal version choice.

After the backup, **Scene Patcher** says `Scene Patcher will not change the backup. Delete it when you no longer need it. It will be published with the mod while it remains there.` The backup stays beside the scene patch file and is included when the mod is published. The patcher does not delete it. Delete it when you no longer need it. Save writes the full editor version, including the scene patch file's content at the start of the session, any unknown content, and your scene patch edits. The backup remains in place.

If the original changes after the backup is written, review its new version before saving. Changing or deleting the verified backup does not return the file to repair while the original stays unchanged.
