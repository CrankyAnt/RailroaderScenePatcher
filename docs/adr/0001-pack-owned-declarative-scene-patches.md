<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Pack-owned declarative scene patches

Each supplying MapMod owns one declarative scene-patch document in its pack data, shared by editor authoring and runtime application. The document describes desired object state and relationships rather than a sequence of edits, so repeated application can reconcile the same authored state without accumulating changes. This keeps patches with the pack that distributes them without rewriting Unity scenes or inherited map packs to store those patches. See [the patch document and target-identity resolution](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/9#issuecomment-6730860470).
