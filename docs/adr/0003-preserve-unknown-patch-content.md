<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Preserve unknown patch content

Runtime reads supported older patch formats without rewriting their files. Editor saves and format conversion preserve unknown fields and constructs without executing them or assigning them new meaning; when safe conversion cannot be established, the source document remains protected from overwrite. This allows newer patch data to survive older editor workflows without treating unknown content as understood. See [the compatibility resolution](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/11#issuecomment-6732010991).
