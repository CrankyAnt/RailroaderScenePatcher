<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Move a board with its terrain effects

Move, rotate, disable, or restore an independent board, such as a location sign, together with its culling and terrain mask. The game then regenerates the affected terrain around it. For the general native map editor workflow, including Save, Undo, and Redo, see [edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md). For the JSON fields, see [scene patch file v1](scene-patch-file-v1.md).

## Check that the object is an independent board

An independent board is a scene object with these parts:

- a `Helpers.Culling.RendererCuller` on the object itself, which the game uses to show and hide it by distance,
- exactly one `Map.Runtime.MaskComponents.CircleMapMask` on the same object, which keeps generated trees and other masked terrain output away from it,
- no track relationship: no `Track.*` component anywhere in its hierarchy.

In the native map editor, open **Scene Patcher**, click **Refresh** in the command band, and search for the board under **Independent board** in the outliner. On the **Object** tab, **Identity** shows the structure `independent-board-v1`, and **Supported edits** describes how you can move, rotate, enable, or disable the board. If it starts with **Not editable:**, the reason follows. The patcher refuses a board in these cases:

- its culler or mask is disabled, or it carries more than one circle mask,
- a parent object is inactive (the board itself may be disabled),
- its mask flattens terrain height,
- its hierarchy contains a `Track.*` component, another culler, another mask component, or another culling handler.

A board placement holds a position and a rotation only. It never names a track, segment, or marker, and the patcher does not attach the board to one.

## Move or rotate the board

1. Inspect the board in **Scene Patcher**.
2. On the **Placement** tab, in **Numeric** mode, enter the position in map coordinates and the rotation in degrees.
3. Click **Apply** at the bottom of the tab.

The patcher moves the board, asks the culler to update its culling position, and rebuilds the circle mask. It then checks that the culling sphere and every mask registration sit at the new position. If they do not, the edit fails and the board returns to its previous state.

The game then regenerates the terrain at both positions. Trees return where the board stood and disappear where it now stands. This takes a moment. On the **Changes** tab, the **Terrain** section shows a message beginning `Terrain pending:` until the game has rebuilt each loaded terrain tile and its terrain work is idle. It then shows `Terrain updated:` followed by the completed positions.

While you inspect the board, the message area reports changes under **Terrain output** as pending, updated, or update stopped if the game takes too long. A tile that hasn't been built needs no rebuild, so its position counts as updated. The game will build it from the board's current mask when it loads.

## Disable or enable the board

Click **Disable** at the bottom of the **Object** tab. The culler and the mask release their registrations through the game's own component lifecycle, and the terrain at the board's position regenerates without the mask. **Enable** registers both again at the board's current position. A disabled board can still be moved; it registers at its new position when you enable it.

An explicit `"active": false` in the scene patch file is applied on every load, so a later activation by the game does not keep a disabled board visible.

## Undo, Redo, and removal

The native map editor's **Undo** and **Redo** restore the board's earlier or later placement, activity, and culling and mask registrations. The terrain regenerates again. **Remove modification** in the footer returns the board to its effective underlying state, including its position in the loaded map with any other modifications in your scene patch file still applied. None of these steps leaves an extra culling sphere or mask registration behind.

## Save and reload

Save writes the board's placement and activity to `Data/scene-patches.json`, together with the native map data and the patcher dependency. Save does not write the terrain output that the board's mask generates, such as the trees removed around it. The game regenerates that output from the board's saved position when the map loads, so a reloaded world shows the same terrain effect without a second copy of the board's registrations. Terrain you edit with the native terrain tools is separate: the native map editor saves those edited terrain tiles through its own Save route, and a board edit does not change them.

Independent-board pattern targets other than the legacy Sylva location sign require patcher 0.5.0 or later. The Sylva target still uses the pattern checks, but does not raise the pattern-based board dependency minimum. Save derives the dependency minimum from the scene patch file's content; see [the scene patch file reference](scene-patch-file-v1.md#dependency-minimum).

## When the terrain output does not complete

If the game has not rebuilt an affected position within 30 seconds, the **Terrain** section shows a message beginning `Terrain update stopped:` with the positions that completed and the positions still outstanding. The board edit stays in the world and in the scene patch file; nothing is reversed. Save is not affected, because Save does not write the generated terrain output.

1. Make sure the area is still loaded, for example by moving the camera back to the board.
2. Click **Retry** on the **Changes** tab. The patcher asks the game to rebuild the outstanding tiles again. The section keeps the positions outstanding until the rebuild completes; it never reports them complete because of the retry alone.
3. If the output still does not complete, save your work and reload the native map editor. Loading regenerates all terrain from the saved board position.

In ordinary play, a terrain output that does not complete produces a warning in the game's message log that names the board, the completed and outstanding positions, and the patcher maintainer as the contact. The board keeps its placement; reloading the map regenerates the output.

## When an edit fails

A failed edit restores the board's previous placement, activity, culling sphere, and mask registration. The restored terrain then regenerates. Until that output completes, the patcher refuses further edits to the board and explains why you must wait.

If the patcher can't verify restoration, or the restored terrain output stops, click **Recheck** in the footer. Further edits stay blocked until the check succeeds. Recheck reapplies the scene patch file's state if the board differs and requests any stopped terrain output again. Edits become available once that output completes. Reloading the native map editor also clears the block.

## Limits

The behavior above rests on research with the Sylva location board on Railroader 0.6.2 build 26335745. In that research, moving the board's circle mask onto a generated tree removed the tree from the generated tile output, and moving it back restored the tree. The automated tests check the patcher's rules against a controlled model of the culler, mask, and terrain system; a recorded run of this patcher version in the game is not yet part of this guide. The patcher reports `Capability.Unchecked` on every build. Other boards are accepted because they match the same component pattern; none has been tested individually. Rendered pixels, GPU tree buffers, height effects, and vegetation configurations other than the tested tree are not established.
