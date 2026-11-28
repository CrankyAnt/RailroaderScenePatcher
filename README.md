# Railroader Scene Patcher

Railroader Scene Patcher is a DLL mod for Railroader. Map modders use it to move or repair supported scene objects. Players install it when a mod they use requires it.

> **Status:** Under development.

Author: CrankyAnt

## Download

There's no official release or mod package yet. Once available, releases will appear on Nexus Mods as the main download source and on [GitHub Releases](https://github.com/CrankyAnt/RailroaderScenePatcher/releases).

## What this mod does

Map modders can move supported objects such as misplaced signs and bridges, and repair track markers with broken bindings. The patcher saves your scene changes with your mod and applies them when players load the target map or a map that inherits it as a layer. For map data such as track and industry spans, use Railroader's native map editor.

## Compatibility

- **Game version.** The patcher requires Railroader 0.6.2 or later. If Steam's default branch is below that version, switch to the Experimental branch. In your Steam Library, right-click Railroader and click **Properties**. Select **Game Versions & Betas**, select **experimental**, and wait for Steam to finish downloading the update. See [Railroader's Experimental branch instructions](https://docs.railroadergame.com/experimental/#switching-to-experimental).
- **Platform.** Railroader's [Steam page](https://store.steampowered.com/app/1683150/Railroader/) lists 64-bit Windows. The installation steps below are for Windows.
- **Multiplayer.** The patcher is designed to apply scene changes on each computer when the target map or an inheriting map loads. The host and each client must install the required versions of the mod and the patcher and enable DLL mods locally. Multiplayer hasn't been tested. See [how each peer applies changes](docs/guides/scene-patches/scene-patch-file-v1.md#application-on-each-peer).

Scene edits have been tested on the base-game map (`rr/bushnell-whittier`, terrain `rr/nc-bryson`) and maps that inherit it as a layer. On Everett, a map with its own terrain, the map and save stayed intact. In play, the patcher skipped a scene patch file for the base-game map and showed a warning. Scene edits on Everett and other maps with a different terrain tile set are untested.

## Install and enable the patcher

You install and update mods and manage DLL permissions on your own computer.

1. Install the mod you want to use and the version of Railroader Scene Patcher it requires. Extract each mod if it came in a ZIP file. Put each mod folder, with `Info.json` at its root, in the default mods folder, `Documents\Railroader\Mods`. If you changed [Railroader's home directory](https://docs.railroadergame.com/game/home-directory/), use the `Mods` folder there.
2. Open Railroader's **Preferences** and select **Features**. In the **Experimental** section, turn on **Allow Unsafe DLL Mods** in 0.6.2 or **Allow DLL Mods** in 0.6.3. In 0.6.3, confirm the change when prompted. This setting lets Railroader run DLL mods on your computer. Only install DLL mods from authors you trust.
3. Quit and reopen Railroader after installing the files, changing DLL permission, or replacing the patcher's DLL. Railroader needs a full restart to pick up those changes.
4. On the main menu, click **Mods** to open **Manage Mods**. Enable the mod that requires the patcher and click **Apply**. Railroader enables its installed dependency automatically. If the dependency is missing, incompatible, or blocked by DLL permission, check the reason shown in **Manage Mods**.

See Railroader's [mod installation guide](https://docs.railroadergame.com/modding/installing-mods/) and [DLL mod settings](https://docs.railroadergame.com/modding/dll-mods/).

## For map modders

See the [modder manual](docs/guides/scene-patches/modder-manual.md) for editing and distributing your own scene changes.

## Limitations

If multiple mods try to change the same existing scene object, an informational notice in the **Log** names the object and the mods. The overlap alone doesn't establish a conflict. The patcher continues in Railroader's load order without choosing a winner. Check the changes listed in each mod's description when deciding whether to use them together.

On some maps, the game creates every scene object the patcher checks while the map is running. The patcher lists no objects to edit on those maps, and its window explains why.

## Diagnostics

If a scene change fails to apply, look for a **Railroader Scene Patcher** warning in the game's **Log** window. If the **Log** can't show the warning, the patcher writes it to the Player log. On Windows, the Player log is at `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\Player.log`.

For more detail, open **Preferences** and select the **Mods** tab. In the **Scene Patcher** section, turn on **Debug logging**, reproduce the problem, then turn the option off again.

## Report an issue

If the warning tells you whom to contact, follow that instruction. For an invalid patch definition, contact the author of the mod that supplied it. The warning may include the author's name and the mod's website. For a patcher fault or an unknown cause, use [GitHub Issues](https://github.com/CrankyAnt/RailroaderScenePatcher/issues). A warning that the patch targets a different map has no reporting instruction.

Include the full warning. It identifies the supplying mod, its ID and version when available, the error, and the patcher and game versions. For an operation failure, it also names the operation and the object if the patcher found it. Include the map you loaded, your Railroader and patcher versions, your operating system, the other mods you have installed, and the steps to reproduce the issue. If you collected a Player log, attach the relevant part. Remove personal information from any warning or log before sharing it.

## License

[MIT License](LICENSE.md) · [File license assignments](REUSE.toml)

## [Changelog](CHANGELOG.md)
