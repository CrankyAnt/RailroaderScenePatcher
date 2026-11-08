<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Boot03 runtime evidence

This isolated editor run used `crankyant/scene-operations-map` on Railroader 0.6.2 experimental build 26335745. The process command line and fresh native log identified RuntimeHome before testing. Every installed authored DLL and manifest matched the [build identity](BuildIdentity.json) frozen before launch. Its `evidenceKind` describes compilation, not the subsequent runtime observations.

[AuthoredSources](AuthoredSources/) retains all twenty independently authored inputs. Each path appends `.txt` to the manifest path and preserves its hashed bytes. These copies identify this run even where later source changes reuse the working path. Vendor binaries, assets and full native logs remain outside the repository.

- [Automatic marker application and restoration limits](MarkerGroups/Report.md)
- [Independent track-generated trestles](Bridge/Report.md)
- [Terrain preflight refusal before mutation](Terrain/Report.md)

The terrain refusal exposed an incorrect preflight expectation: 2.5 m was the culling radius, while the native mask radius was 1.5 m with 1 m falloff. The corrected probe ran in a separate [Boot04 process](../Boot04/README.md). No replacement DLL was loaded into this running process.

The marker's native placement method returned false during restoration. The probe subsequently restored captured pose, binding, registration and group state through separate operations. This is a partial native restoration result, despite the UI success label. The world was discarded without saving after the bridge was restored. The final [neutral-state verification](../NeutralStateVerification.txt) records the empty test home after Boot04.
