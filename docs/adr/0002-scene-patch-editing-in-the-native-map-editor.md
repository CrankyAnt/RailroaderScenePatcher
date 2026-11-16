<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene patch editing in the native map editor, with its history

Scene patch editing takes place directly in the native map editor, using native terminology and Undo/Redo for completed operations. A native map editor reload ends the current native history; the patcher does not preserve that history across reload. This keeps scene patch editing within the native map editor's existing interaction and history model. See [the scene patch editing resolution](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/8#issuecomment-6729696383).
