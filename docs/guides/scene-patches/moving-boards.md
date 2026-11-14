<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move a board with its terrain effects

This guide shows how to move, rotate, disable or restore an independent board, such as a location sign, and what happens to its culling, terrain mask and the generated terrain around it. For the general editor workflow, Save, Undo and Redo, see [edit and save fixed placement in the map editor](editing-placement-in-the-editor.md). For the JSON fields, see [scene-patch document v1](scene-patch-document-v1.md).

## Check that the object is an independent board

An independent board is a scene object with these parts:

- a `Helpers.Culling.RendererCuller` on the object itself, which the game uses to show and hide it by distance,
- exactly one `Map.Runtime.MaskComponents.CircleMapMask` on the same object, which keeps generated trees and other masked terrain output away from it,
- no track relationship: no `Track.*` component anywhere in its hierarchy.

In the map editor, open **Scene patches**, select **Refresh** in the command band and search for the board in the outliner. Its row sits under the **Independent board** folder. The **Identity** section of the **Object** tab shows the structure `independent-board-v1`, and **Supported edits** in the **State** section shows **Fixed placement and activity**. If **Supported edits** says **Inspection only until corrected**, it also names the reason. The patcher refuses a board in these cases:

- its culler or mask is disabled, or it carries more than one circle mask,
- a parent object is inactive (the board itself may be disabled),
- its mask flattens terrain height,
- its hierarchy contains a `Track.*` component, another culler, another mask component or another culling handler.

A board placement holds a position and a rotation only. It never names a track, segment or marker, and the patcher does not attach the board to one.

## Move or rotate the board

1. Inspect the board in **Scene patches**.
2. On the **Placement** tab, in **Numeric** mode, enter the position in map coordinates and the rotation in degrees.
3. Select **Apply** in the footer.

The patcher moves the board, asks the culler to update its culling position and rebuilds the circle mask. It then checks that the culling sphere and every mask registration sit at the new position. If they do not, the edit fails and the board returns to its previous state.

The game then regenerates the terrain at the old and the new position: trees return where the board stood and disappear where it now stands. This takes a moment. The **Terrain output** section of the **Changes** tab shows **Terrain output outstanding**, with the affected positions, until the game has rebuilt each loaded terrain tile and its terrain work is idle. While the board is inspected, the message area reports each change under the source **Terrain output**: outstanding, then complete, or stopped when the game does not finish in time. It then shows **Terrain output complete**. A position whose tile is not built needs no rebuild; the game builds that tile from the board's current mask when it loads, and the section counts that position as complete.

## Disable or enable the board

Select **Disable** in the footer of the **Object** tab. The culler and the mask release their registrations through the game's own component lifecycle, and the terrain at the board's position regenerates without the mask. **Enable** registers both again at the board's current position. A disabled board can still be moved; it registers at its new position when you enable it.

An explicit `"active": false` in the document is applied on every load, so a later activation by the game does not keep a disabled board visible.

## Undo, Redo and removal

The editor's **Undo** and **Redo** return the board to the earlier or later pose and activity, with its culling and mask registrations, and the terrain regenerates again. **Remove** on the **Changes** tab returns the board to its effective underlying state: its position in the loaded map, with any other modifications in your document still applied. None of these steps leaves an extra culling sphere or mask registration behind.

## Save and reload

Save writes the board's placement and activity to `Data/scene-patches.json`, together with the native map data and the patcher dependency. Save does not write the terrain output that the board's mask generates, such as the trees removed around it. The game regenerates that output from the board's saved position when the map loads, so a reloaded world shows the same terrain effect without a second copy of the board's registrations. Terrain you edit with the native terrain tools is separate: the editor saves those edited terrain tiles through its own Save route, and a board edit does not change them.

Independent-board pattern targets other than the legacy Sylva location sign require patcher 0.5.0 or later. The Sylva target still uses the pattern checks, but does not raise the pattern-based board dependency minimum. Save derives the dependency minimum from the document's content; see [the document reference](scene-patch-document-v1.md#dependency-minimum).

## When the terrain output does not complete

If the game has not rebuilt an affected position within 30 seconds, the **Terrain output** section shows **Terrain output stopped**, with the positions that completed and the positions still outstanding. The board edit stays in the world and in the document; nothing is reversed. Save is not affected, because Save does not write the generated terrain output.

1. Make sure the area is still loaded, for example by moving the camera back to the board.
2. Select **Retry** on the **Changes** tab. The patcher asks the game to rebuild the outstanding tiles again. The section keeps the positions outstanding until the rebuild completes; it never reports them complete because of the retry alone.
3. If the output still does not complete, save your work and reload the editor. Loading regenerates all terrain from the saved board position.

In ordinary play, a terrain output that does not complete produces a warning in the game's message log that names the board, the completed and outstanding positions, and the patcher maintainer as the contact. The board keeps its placement; reloading the map regenerates the output.

## When an edit fails

A failed edit restores the board's previous pose, activity, culling sphere and mask registration. The restored terrain then regenerates. Until it has, the patcher refuses further edits of that board and says so; the next edit is accepted once the restored terrain output completes. If the patcher cannot verify the restoration, or the restored terrain output stops, it blocks the board until you select **Recheck** on the **Changes** tab and the check succeeds. Recheck applies the document's state again if the board differs, requests the stopped terrain output again, and accepts edits once that output completes. Reloading the editor also clears the block.

## Limits

The behavior above rests on research with the Sylva location board on Railroader 0.6.2 build 26335745. In that research, moving the board's circle mask onto a generated tree removed the tree from the generated tile output, and moving it back restored the tree. The automated tests check the patcher's rules against a controlled model of the culler, mask and terrain system; a recorded run of this patcher version in the game is not yet part of this guide. The patcher reports `Capability.Unchecked` on every build. Other boards are accepted because they match the same component pattern; none has been tested individually. Rendered pixels, GPU tree buffers, height effects and vegetation configurations other than the tested tree are not established.
