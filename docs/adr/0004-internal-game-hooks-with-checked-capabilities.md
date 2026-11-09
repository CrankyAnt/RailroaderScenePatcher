<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Internal game hooks within checked capabilities

The patcher may depend on internal editor and runtime hooks within an explicit compatibility boundary. It checks the functions and prerequisites for an affected capability, so an unchecked build number alone does not block operation while a demonstrated incompatibility disables that capability. These hooks are not public game APIs and carry cross-build compatibility cost. See [the compatibility resolution](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/11#issuecomment-6732010991).
