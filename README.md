# Railroader Scene Patcher

A Railroader mod for repairing and moving supported scene objects. Map modders can make these changes in the native Map Editor or in JSON. When a player loads a map, the patcher applies the changes supplied by their enabled mods.

> **Status:** Under development. Not yet available for use.

Author: CrankyAnt

## What this mod does

For map makers whose track changes leave existing signs, bridges or other scene objects in the wrong place, or break their track-marker references.

Players using mods that depend on Railroader Scene Patcher need to keep it installed and enabled for those changes to take effect.

Tests cover the base-game map (`rr/bushnell-whittier`, terrain `rr/nc-bryson`) and maps that inherit it as a map layer. Maps with a different terrain tile set are untested. For editing instructions, see the [modder manual](docs/guides/scene-patches/modder-manual.md).

If a patch fails while loading a map, the game warning identifies the supplying mod, its identifier and version, the operation, object and error, plus the patcher and game versions. Send invalid definitions to the mod author. Send unknown failures or a demonstrated patcher fault to the patcher maintainer. The warning may include author and website details supplied by the mod. The patcher does not provide a separate support channel.

With several such mods, the patcher applies their changes in Railroader's load order. If two of them change the same object, the game shows an information message that names the object and the mods. It means the combination might not work as either author intended; it is not an error and does not mean they conflict. The patcher does not choose between them: where both set the same property, the mod loaded later sets its final value. Check the mods' descriptions to decide whether to use them together.

## License

This repository uses per-file license assignment according to the REUSE
specification. See [REUSE.toml](REUSE.toml) for the authoritative
machine-readable record.

Every file is licensed under the [MIT License](LICENSE.md), free to use,
modify, and redistribute. If you copy or build on this project, retain the
original copyright and attribution to CrankyAnt as required by the MIT license.

## [Changelog](CHANGELOG.md)
