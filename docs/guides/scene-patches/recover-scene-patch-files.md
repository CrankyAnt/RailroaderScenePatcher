<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Recover a protected scene patch file

Use this guide when scene patch editing reports that the edited mod's `Data/scene-patches.json` is protected from overwrite. A scene patch file is protected when it cannot be read, declares a missing, unrecognized or newer format version, names another map, or holds numbers whose text cannot be kept exactly. The native map editor's Save then never writes it. When the mod's `Info.json` cannot be read, scene patch editing is unavailable as well; correct `Info.json` and use **Read again**. **Start fresh** is not offered then, because the scene patch file is not the problem.

When a readable scene patch file changes outside scene patch editing, you can choose **Use file version** or **Keep editor version**. If the file is malformed, unreadable or has an unsupported version, Scene patches enters **File needs repair** instead. **Keep editor version** requires a verified backup before Save can overwrite the file.

If the scene patch file was deleted outside scene patch editing, **Keep editor version** needs no backup. The next Save writes the editor version.

## Read what is wrong

1. Open **Scene patches** in the native map editor and open its session details with the page icon in the title bar.
2. The **Scene patch file** section starts with the reason the scene patch file is protected. Below it, each problem shows:
   - the JSON path, such as `sourceMap.mapId` or `modifications.place-board`;
   - the line or lines in the file, or **none** when a field is missing;
   - **Text there**: the lines at that place, at most eight lines and 1,200 characters; a longer passage ends with the number of lines left out;
   - **Expected**: the structure or value the format expects.
3. Select **Format reference** to open the [scene patch file reference](scene-patch-file-v1.md) and **Recovery guide** to open this guide.

The section also lists modifications of a readable scene patch file that the patcher does not execute, for example content a newer patcher version wrote. Those modifications stay in the scene patch file unchanged.

## Correct the file and read it again

1. Save your native map work and reopen the native map editor if you edited the map in this session. Recovery refuses unsaved native work and a session whose Undo history has changed, so no history is replaced.
2. Correct `Data/scene-patches.json` in a text editor. The patcher guesses no target and substitutes no nearby object: for a missing or ambiguous target, check the intended object with its [inventory identity](inspect-scene-objects.md). For a scene patch file of a newer format version, use the patcher version that wrote it if you need its content. Keep content you do not understand unless you mean to remove it.
3. In the **Scene patch file** section, select **Read again**. Read the review, then select **Read again** to confirm, or **Cancel** to change nothing.

When the corrected scene patch file reads, it becomes the scene patch file of this session and its modifications are applied to the objects at once, as when the native map editor opens. The footer says how many applied and names those that did not. The file is not written. You can edit and save the scene patch file; an edit changes only the field you edit and keeps the scene patch file's other values. The inventory and the supplied definitions are read again. When the scene patch file still has a problem, it stays protected and the section shows the new diagnostics.

## Start fresh

Start fresh only when you mean to remove all content of the scene patch file, also content this patcher does not understand.

1. Save your native map work and reopen the native map editor if you edited the map in this session.
2. In the **Scene patch file** section, select **Start fresh**. Read the review, then select **Back up and start fresh**, or **Cancel** to change nothing.
3. The patcher copies the original file byte for byte to `scene-patches.json.backup-<id>` beside it and checks the copy before anything else happens. This also works when the file is not valid text. When the copy fails, the scene patch file stays protected. The section and the footer show the backup path.
4. Select the native map editor's **Save**. Save replaces the original with an empty scene patch file of format version 1 that names the bottom map layer of your map. The backup stays; the patcher never deletes or restores it.

When the original or the backup changed after **Back up and start fresh**, Save refuses to replace the original and keeps both files. Reopen the native map editor to read the scene patch file again.

## Keep content you do not understand

Unknown fields at the scene patch file root and inside known objects, and modifications the patcher does not execute, survive every read, edit and Save. A modification with such content cannot be edited or removed in the native map editor; correct or remove it in the file and read the scene patch file again.

Save adds or raises the patcher dependency in `Info.json` only when the scene patch file needs it. It keeps a stricter minimum and every other constraint, adds no upper bound, and reports a constraint that excludes the needed version instead of changing it.

## Keep the editor version while the file needs repair

Choose **Keep editor version** when the file needs repair and you want to replace it with the session's loaded scene patch file and edits. The review says that the patcher will write a backup at the displayed relative path before overwriting the file. Confirm only if the backup should be kept with the mod.

The patcher writes a byte-for-byte copy beside the changed file and verifies it before enabling Save. If it cannot write or verify the backup, the original stays protected. The review shows a new backup path before you retry. After a successful backup, the diagnosis remains visible with line numbers matching the backup. **Copy to clipboard** copies the full diagnosis. Rechecks do not return that file to repair unless its content changes again. If a check finds a readable but still changed file, Scene patches says `The file is readable again.` and offers the normal version choice.

After the backup, Scene patches states: `Scene Patcher will not change the backup. Delete it when you no longer need it. It will be published with the mod while it remains there.` The backup remains beside the mod file and is included when the mod is published. Scene patch editing does not delete it; delete it when you no longer need it. Save then writes the full editor version: the scene patch file as it was when the session started, including unknown content, plus the session's patch edits. The backup remains in place.

If the original changes after the backup is written, review its new version before saving. Changing or deleting the verified backup does not return the file to repair while the original stays unchanged.
