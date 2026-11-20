# Railroader Scene Patcher

A Railroader mod that lets map modders repair or move supported scene objects, such as misplaced signs or bridges and track markers with broken bindings. If a mod you use requires Railroader Scene Patcher, you need the patcher installed so Railroader can apply that mod's changes when you load a map.

> **Status.** Under development. There is no official release or mod package yet.

Author: CrankyAnt

## Install and enable the patcher

1. Install the mod you want to use and the version of Railroader Scene Patcher it requires. Extract each mod if it came in a ZIP file. Put each mod folder, with `Info.json` at its root, in `Documents\Railroader\Mods`.
2. In Railroader, go to **Preferences > Features > Experimental** and turn on **Allow DLL Mods**. This setting lets Railroader run DLL mods on your computer. Only install DLL mods from authors you trust.
3. Quit and reopen Railroader after installing the files, changing DLL permission, or replacing the patcher's DLL. A full restart lets Railroader pick up the files and the permission setting.
4. On the main menu, click **Mods** to open **Manage Mods**. Enable the mod that requires the patcher and click **Apply**. Railroader enables its installed dependency automatically. If the dependency is missing, incompatible, or blocked by DLL permission, check the reason shown in **Manage Mods**.

You install and update the mods and manage DLL permission on your own computer. In multiplayer, the host and each client need the required mod versions installed and DLL permission enabled locally. The patcher applies scene changes on each computer when the map loads.

See Railroader's [mod installation guide](https://docs.railroadergame.com/modding/installing-mods/) and [DLL mod settings](https://docs.railroadergame.com/modding/dll-mods/).

## Report an application failure

If a scene change fails to apply, look for a **Railroader Scene Patcher** warning in the game's **Log** window. Include the full warning, the map you loaded, and how to reproduce the failure. The warning identifies the mod that supplied the change, its ID and version when available, the operation, the object if resolved, the error, and the patcher and game versions.

Follow the **Report to** instruction in the warning. For an invalid definition, contact the author of the mod that supplied the change. The warning can include the mod's author and published website. For an unknown cause or a patcher fault, contact the patcher maintainer through [GitHub Issues](https://github.com/CrankyAnt/RailroaderScenePatcher/issues).

## Understand an overlap notice

If multiple mods try to change the same existing scene object, an informational notice in the **Log** names the object and the mods. The notice flags possible incompatibility. It doesn't prove a conflict. The patcher continues in Railroader's load order without choosing a winner. Check the changes listed in each mod's description when deciding whether to use them together.

## Tested maps

The patcher has been tested on the base-game map (`rr/bushnell-whittier`, terrain `rr/nc-bryson`) and on maps that inherit it as a map layer. Maps with a different terrain tile set, such as a whole new map, are untested. The patcher stays enabled on these maps unless the end-of-project check finds that it corrupts the map or its save.

## For map modders

See the [modder manual](docs/guides/scene-patches/modder-manual.md) for editing and distributing your own scene changes.

## License

This repository uses per-file license assignment according to the REUSE
specification. See [REUSE.toml](REUSE.toml) for the authoritative
machine-readable record.

Every file is licensed under the [MIT License](LICENSE.md), free to use,
modify, and redistribute. If you copy or build on this project, retain the
original copyright and attribution to CrankyAnt as required by the MIT license.

## [Changelog](CHANGELOG.md)
