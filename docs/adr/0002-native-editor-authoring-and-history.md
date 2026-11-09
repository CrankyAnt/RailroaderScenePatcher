<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Native editor authoring and history

Scene-patch authoring takes place directly in the map editor, using native terminology and Undo/Redo for completed operations. An editor reload ends the current native history; the patcher does not preserve that history across reload. This keeps authoring within the editor's existing interaction and history model. See [the authoring resolution](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/8#issuecomment-6729696383).
