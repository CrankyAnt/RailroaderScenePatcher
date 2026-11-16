<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Mod-owned declarative scene patches

Each supplying mod owns one declarative patch document in its mod data, shared by scene patch editing and runtime application. The document describes desired object state and relationships rather than a sequence of edits, so repeated application can reconcile the same mod values without accumulating changes. This keeps patches with the mod that distributes them without rewriting Unity scenes or mods that supply map layers to store those patches. See [the patch document and target-identity resolution](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/9#issuecomment-6730860470).
