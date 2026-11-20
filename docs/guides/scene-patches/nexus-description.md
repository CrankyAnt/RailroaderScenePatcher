<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Nexus Mods description

## Summary

Required for Railroader mods that use Scene Patcher to repair or move scene objects.

## Description

Railroader Scene Patcher lets map modders repair or move supported scene objects, such as misplaced signs or bridges and track markers with broken bindings. Install the patcher if a mod you use requires it. Railroader then uses it to apply that mod's changes when you load a map.

By CrankyAnt.

### Install and enable the patcher

1. Install the mod you want to use and the version of Railroader Scene Patcher it requires. Extract each mod if it came in a ZIP file. Put each mod folder, with `Info.json` at its root, in `Documents\Railroader\Mods`.
2. In Railroader, go to **Preferences > Features > Experimental** and turn on **Allow DLL Mods**. This setting lets Railroader run DLL mods on your computer. Only install DLL mods from authors you trust.
3. Quit and reopen Railroader after installing the files, changing DLL permission, or replacing the patcher's DLL. Railroader needs a full restart to read the new files and permission setting.
4. On the main menu, click **Mods** to open **Manage Mods**. Enable the mod that requires the patcher and click **Apply**. Railroader enables its installed dependency automatically. If the dependency is missing, incompatible, or blocked by DLL permission, check the reason shown in **Manage Mods**.

Install and update mods and manage DLL permission on your own computer. In multiplayer, the host and each client must install the required mod versions and enable DLL permission on their own computer. The patcher applies scene changes on each computer when the map loads.

See Railroader's [mod installation guide](https://docs.railroadergame.com/modding/installing-mods/) and [DLL mod settings](https://docs.railroadergame.com/modding/dll-mods/).

### Report an application failure

If a scene change fails to apply, look for a **Railroader Scene Patcher** warning in the game's **Log** window. In your report, include the full warning, the map you loaded, and steps to reproduce the failure. The warning identifies the mod that supplied the change, its ID and version when available, the operation, the object if resolved, the error, and the patcher and game versions.

Follow the **Report to** instruction in the warning. For an invalid definition, contact the author of the mod that supplied the change. The warning can include the mod's author and published website. For an unknown cause or a patcher fault, contact the patcher maintainer through [GitHub Issues](https://github.com/CrankyAnt/RailroaderScenePatcher/issues).

### Understand an overlap notice

If multiple mods try to change the same existing scene object, an informational notice in the **Log** names the object and the mods. They might be incompatible, but the notice doesn't prove a conflict. The patcher continues in Railroader's load order without choosing a winner. Check each mod's description of its changes when deciding whether to use them together.

### Tested maps

The patcher has been tested on the base-game map (`rr/bushnell-whittier`, terrain `rr/nc-bryson`) and on maps that inherit it as a map layer. Maps with a different terrain tile set, such as a whole new map, are untested. The patcher doesn't disable itself on these maps.

### For map modders

See the [modder manual](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/c23ecc184afa21a1b7cdaefe9ee171ef86db720c/docs/guides/scene-patches/modder-manual.md) for editing and distributing your own scene changes.
