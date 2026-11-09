# Early repair observer

This disposable research DLL observes M5pp immediately before and after `TrackMarker.OnEnable`. It does not change marker state, remove messages or suppress the original method. It uses [Lib.Harmony 2.4.2](https://www.nuget.org/packages/Lib.Harmony/2.4.2), an external dependency, not a game-supported extension contract. Its MIT license and dependencies are distributed with the NuGet package. Neither the package nor vendor assemblies belong in this repository.

Build with .NET SDK and the pinned package, placing outputs outside the checkout:

```powershell
dotnet build Research/EarlyRepairProbe/EarlyRepairProbe.csproj -p:GameManaged='D:\SteamLibrary\steamapps\common\Railroader\Railroader_Data\Managed' -p:BaseIntermediateOutputPath='C:\TMP\EarlyRepairProbe\obj\' -p:OutputPath='C:\TMP\EarlyRepairProbe\bin\'
```

The 2026-11-09 build completed with zero warnings and errors. `EarlyRepairProbe.dll` SHA-256 is `B12CC6D04C99E71D9E75DF922F393024B90BB7DA55E749166E4DA2B68A52B41B` and `0Harmony.dll` is `7B9E756306FA3D7620E02A857C8927A6AB04973F9BD8A77D3866700A6DEAC55C`. Verify hashes before use; hashes may differ after a rebuild. The bounded ordinary-world trial is recorded in the research report.

## Smallest ordinary-save observation

An operator in the owning chat must obtain the exclusive game lease and follow the canonical local `.docs/TestEnvironment.md`. Confirm the installed binary hash and an inactive disposable profile. Recover the broken-marker fixture from immutable commit `3e5970722bb278cbbaee003300ada6ce916df702`, `Research/BrokenMarkerProbe/Fixture/`, and use its corresponding disposable existing save, if available. Verify the selected map and marker before interpreting results. Do not use personal saves. Install only `Info.json`, the authored DLL and its matching `0Harmony.dll` together in a dedicated temporary mod folder in the isolated home, enable only the necessary fixture and probe, and restart the game. If dependency resolution fails, stop; do not copy dependencies into the game installation.

Record whether `OnModLoad` and `patch installed` occur before marker activation. Load the disposable existing save, record the marker warning and the native Log window as actually shown to the player, then exit without saving. Inspect the observer's local log for paired `before` and `after` entries for the same instance. `graph=True` and `destination=True` at `before` support availability of the graph and the named segment only. `locationPresent` reports a non-null location, not its validity. `fixturePackMatches` counts the fixture pack in the internal ordered list and does not establish patch ownership. They do not establish patch discovery, correct ownership, destination validity, safe rebind, or downstream consumers. A missing entry does not prove nonexistence: the hook may have failed or installed too late. Compare marker instance, map and native warning with the independent control and later lifecycle evidence. Preserve selected non-sensitive observations only. Restore the test home to its neutral state after the test.

The probe does not create a control warning or observe window lifecycle. For the separate fallback question, use the earlier frozen warning-control probe and a dedicated runtime protocol that distinguishes an auto-opened window from a player-opened window, with and without an unrelated warning. Do not close or suppress the window based solely on this observer. Editor entry is a separate trial.
