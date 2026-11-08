<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Boot04 runtime evidence

This isolated editor run used `crankyant/scene-operations-map` on Railroader 0.6.2 experimental build 26335745. The process command line and fresh native log identified RuntimeHome before testing. Every installed authored DLL and manifest matched the [build identity](BuildIdentity.json) frozen before launch. Its `evidenceKind` describes compilation, not the subsequent runtime observations.

[AuthoredSources](AuthoredSources/) retains all twenty independently authored inputs with `.txt` appended to their original paths. Their bytes match the source hashes in the manifest. Only the [terrain output trial](Terrain/Report.md) mutated the world in this process. It exercised the corrected mask-radius guard and moved the board to one observed generated tree within 100 m, then restored it.

The native terrain update removed one tree from generated data handed to the loaded GPUI consumer. Restoration returned the same sampled tree signature. Pixel-level disappearance of that tree was not observed. The original board area was visible before movement and after restoration. No ordinary-save lifecycle, arbitrary origin shift or other terrain configuration was tested.

The final authored log records `OnWorldUnload dirty=False`. The world and game were exited without saving. The [neutral-state verification](../NeutralStateVerification.txt) records no game process, empty active Mods, ModDev and Saves folders, and preferences byte-identical to the neutral test backup. The case library, staged packs and local observations remain available; the exclusive game lease was released.
