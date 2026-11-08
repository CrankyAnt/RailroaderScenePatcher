<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Boot02 runtime evidence

This isolated editor run used `crankyant/scene-operations-map` on Railroader 0.6.2 experimental build 26335745. The game reported the isolated RuntimeHome as its active home. Steam appended another home argument; the native first-argument selection and the active-home log were checked before the trial.

[BuildIdentity.json](BuildIdentity.json) is the exact manifest frozen before launch. Its `evidenceKind` describes the build step, not the subsequent runtime findings. Every installed authored DLL was checked against that manifest. [AuthoredSources](AuthoredSources/) retains its twenty independently authored inputs with `.txt` appended to their original paths. Removing that final suffix gives the path in the manifest; the retained bytes match its SHA-256. These copies preserve the executed source when later probe changes use the same working paths. No vendor binaries or scene assets are retained here.

- [Marker placement and groups](MarkerGroups/Report.md)
- [Milepost cache and display](Milepost/Report.md)
- [Crossing generated geometry](CrossingPad/README.md)
- [Bridge contact and scenery](Bridge/Report.md)
- [Terrain output](Terrain/Report.md)

Each report distinguishes state telemetry from observed rendering and states its limits. Local screenshots and private native logs remain outside the repository. Their hashes identify observations retained locally; a hash does not make the image available to another reader.

Every tested mutation was restored before leaving its case. The editor world was unloaded and the game process exited without saving. RuntimeHome/Saves was empty afterwards. This does not establish restoration of unsampled consumers or arbitrary floating-origin changes. Boot02 did not identify the independent timber approach, and its small terrain displacement did not cover a generated tree. Later [trestle observations](../Boot03/Bridge/Report.md) and the [target-tree trial](../Boot04/Terrain/Report.md) address those gaps within their stated limits. The final [neutral-state verification](../NeutralStateVerification.txt) covers the isolated home after the batch.

Boot01 is exploratory evidence with an unresolved association between its executed DLLs and exact source. It must not substitute for this frozen run or for pending observations.
