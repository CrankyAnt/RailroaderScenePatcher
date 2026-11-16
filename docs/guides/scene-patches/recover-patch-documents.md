<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Recover a protected patch document

Use this guide when scene patch editing reports that the edited mod's `Data/scene-patches.json` is protected from overwrite. A document is protected when it cannot be read, declares a missing, unrecognized or newer format version, names another map, or holds numbers whose text cannot be kept exactly. The native map editor's Save then never writes it. When the mod's `Info.json` cannot be read, scene patch editing is unavailable as well; correct `Info.json` and use **Read again**. **Start fresh** is not offered then, because the patch document is not the problem.

## Read what is wrong

1. Open **Scene patches** in the native map editor and open its session details with the page icon in the title bar.
2. The **Patch document** section starts with the reason the document is protected. Below it, each problem shows:
   - the JSON path, such as `sourceMap.mapId` or `modifications.place-board`;
   - the line or lines in the file, or **none** when a field is missing;
   - **Text there**: the lines at that place, at most eight lines and 1,200 characters; a longer passage ends with the number of lines left out;
   - **Expected**: the structure or value the format expects.
3. Select **Format reference** to open the [patch document reference](patch-document-v1.md) and **Recovery guide** to open this guide.

The section also lists modifications of a readable document that the patcher does not execute, for example content a newer patcher version wrote. Those modifications stay in the document unchanged.

## Correct the file and read it again

1. Save your native map work and reopen the map editor if you edited the map in this session. Recovery refuses unsaved native work and a session whose Undo history has changed, so no history is replaced.
2. Correct `Data/scene-patches.json` in a text editor. The patcher guesses no target and substitutes no nearby object: for a missing or ambiguous target, check the intended object with its [inventory identity](inspect-scene-objects.md). For a document of a newer format version, use the patcher version that wrote it if you need its content. Keep content you do not understand unless you mean to remove it.
3. In the **Patch document** section, select **Read again**. Read the review, then select **Read again** to confirm, or **Cancel** to change nothing.

When the corrected document reads, it becomes the document of this session and its modifications are applied to the objects at once, as when the map editor opens. The footer says how many applied and names those that did not. The file is not written. You can edit and save the document; an edit changes only the field you edit and keeps the document's other values. The inventory and the supplied definitions are read again. When the document still has a problem, it stays protected and the section shows the new diagnostics.

## Start fresh

Start fresh only when you mean to remove all content of the patch document, also content this patcher does not understand.

1. Save your native map work and reopen the map editor if you edited the map in this session.
2. In the **Patch document** section, select **Start fresh**. Read the review, then select **Back up and start fresh**, or **Cancel** to change nothing.
3. The patcher copies the original file byte for byte to `scene-patches.json.backup-<id>` beside it and checks the copy before anything else happens. This also works when the file is not valid text. When the copy fails, the document stays protected. The section and the footer show the backup path.
4. Select the native map editor's **Save**. Save replaces the original with an empty document of format version 1 that names the bottom map layer of your map. The backup stays; the patcher never deletes or restores it.

When the original or the backup changed after **Back up and start fresh**, Save refuses to replace the original and keeps both files. Reopen the map editor to read the document again.

## Keep content you do not understand

Unknown fields at the document root and inside known objects, and modifications the patcher does not execute, survive every read, edit and Save. A modification with such content cannot be edited or removed in the map editor; correct or remove it in the file and read the document again.

Save adds or raises the patcher dependency in `Info.json` only when the document needs it. It keeps a stricter minimum and every other constraint, adds no upper bound, and reports a constraint that excludes the needed version instead of changing it.
