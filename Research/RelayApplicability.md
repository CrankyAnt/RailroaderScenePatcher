<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Relay applicability

Research for [issue #65](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/65), informing [issue #38](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/38). Official sources were checked on 2026-11-10. This report assesses capabilities; it does not select an implementation language.

[Specification #38](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/38) does not mandate C#. It defines behavior and responsibility boundaries without requiring a particular class or assembly layout. [Ticket #39](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/39) likewise specifies outcomes rather than an implementation language.

## Language and context

[Relay](https://docs.railroadergame.com/modding/relay/) is Railroader's dynamically typed scripting language. Its syntax is influenced by Swift and C#, and its bytecode runtime particularly by Wren. The developers describe influences, not direct descent. They cite data-marshalling performance, syntax and security as reasons for creating it, and use it in their internal runtime test suite.

The [0.6.0 release notes](https://docs.railroadergame.com/release-notes/0-6-0/) introduce official modding and describe Relay for dynamic mod behavior and base-game equipment customization. On the research date, the [experimental branch page](https://docs.railroadergame.com/experimental/) said 0.6 was on the opt-in experimental branch; release status can change.

The patcher's primary targets include existing Unity scene objects for which native map editing has not established sufficient control. They must be distinguished from ordinary map JSON objects. The [native capability audit](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/8d3bfc0005d15e8b0404fc584479bd60f385c4fd/Research/NativeCapabilityAudit.md) records examples and their evidence limits, including a retained location board and a protected road crossing controller.

## Applicability

| Responsibility | Evidence and assessment |
| --- | --- |
| Pre-load data | [Data.rl](https://docs.railroadergame.com/modding/data-rl/) runs `modifyDefinitions` at startup and `modifyMap` on each map JSON load after parsing and before deserialization. It can merge pack-local JSON or mutate the dictionary. The [map guide](https://docs.railroadergame.com/modding/map-modding/) gives track and industry examples. This is a supported candidate for suitable map data, not evidence of access to arbitrary existing Unity scene objects or their bindings. Arrays replace on merge and later packs win conflicting changes. |
| Ordinary-play lifecycle | [Main.rl](https://docs.railroadergame.com/modding/main-rl/) provides world load and unload callbacks on each peer with the pack. Its guidance distinguishes interaction-driven property writes by the interacting peer from shared game-event work requiring one authority, commonly the host. These rules do not make local scene application host-only. Re-entry, cleanup, identity and equivalence for a specific patch remain untested. |
| Scene lookup and transforms | The [0.6 release notes](https://docs.railroadergame.com/release-notes/0-6-0/) mention `world.getMapObject`; the reviewed [World reference](https://docs.railroadergame.com/modding/relay-api/railroader/world/) does not describe it or its return type. The metadata check below confirms a method signature but not object scope or behavior. [Component definition fields](https://docs.railroadergame.com/modding/components/) include transforms and enabled state, but do not establish arbitrary scene-object writes. |
| Registration, culling, masks and terrain | The reviewed [Relay API index](https://docs.railroadergame.com/modding/relay-api/) and [World reference](https://docs.railroadergame.com/modding/relay-api/railroader/world/) do not establish the required scene lifecycle operations. This is an evidence gap, not proof that an operation is impossible. |
| Protected road crossings | The [Track API](https://docs.railroadergame.com/modding/relay-api/railroader/track/) documents segment, location and switch operations, but the reviewed reference does not establish access to retained road crossing detection circuits or construction of their equipment and bells. The [map guide](https://docs.railroadergame.com/modding/map-modding/) mentions editable diamond track crossings; that does not establish control of a protected road crossing. |
| Editor and UI | [Main.rl](https://docs.railroadergame.com/modding/main-rl/) documents runtime UI extensions and commands. [Data.rl](https://docs.railroadergame.com/modding/data-rl/) explicitly does not run in the editor, and the [map guide](https://docs.railroadergame.com/modding/map-modding/) says editor sessions do not apply or save `modifyMap` mutations. The reviewed sources do not establish native map-editor selection, history, undo or Save integration. |
| Persistence | The [modding overview](https://docs.railroadergame.com/modding/) says Relay cannot write files or open files outside the asset-pack system. [Data.rl](https://docs.railroadergame.com/modding/data-rl/) documents pack-scoped JSON reads. [Properties](https://docs.railroadergame.com/modding/relay-api/railroader/properties/) provides persistent game-object state, but does not establish arbitrary editor-owned file or patch-document persistence. |

Relay is sandboxed, but the [modding overview](https://docs.railroadergame.com/modding/) notes that scripts can still damage internal game data. [DLL mods](https://docs.railroadergame.com/modding/dll-mods/) are compiled .NET code without that sandbox; the documented loading mechanism is experimental and disabled by default. The supported DLL contract is small, and other internal interfaces may change.

## Earlier consideration

The earlier [editor authoring hooks research](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/5d9e6fbd12d8abd8c2e8212428c5b59bc179c473/Research/EditorAuthoringHooks.md) examined the documented Relay UI extension points for an editor inspector and found no documented map-editor point. The [native capability audit](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/8d3bfc0005d15e8b0404fc584479bd60f385c4fd/Research/NativeCapabilityAudit.md) considered `setFeatureEnabled` narrowly and did not treat it as a documented progression rewrite or scene-object editing API. Those checks considered Relay for particular questions; they did not establish a complete architectural comparison.

## Installed metadata

On 2026-11-10, .NET `MetadataLoadContext` was used to inspect assembly metadata without executing game code. The inspected baseline was recorded as Railroader 0.6.2, build 26335745. `Railroader.dll` SHA-256 was `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`; `Relay.dll` SHA-256 was `2B67B538740E816D7F06C1809F1C9FEA3F67FCC8BE394345BE88988BCB411454`.

Metadata declares `Game.Scripting.Modules.Railroader.World.getMapObject(string)` returning `Relay.Value`. A nearby `NativeMapObject` has `Wrap(string)`, a class-definition factory and a nested handle with a string `Id`. Signatures alone do not establish that the method returns this wrapper, what the dynamically constructed class exports, or which identifiers it accepts. They do not prove lookup of the retained Sylva board, scene transform or activity control, or registration, culling, mask, terrain or crossing lifecycle behavior. Method presence is not functional proof or proof of supported API status.

## Conclusion

Pack-local map JSON patches and ordinary-play callbacks are documented opportunities for suitable work. No advantage for a specific current patch operation has been demonstrated. A hybrid using Relay for supported data and another component for scene or editor operations is conceivable, but its handoff, ownership and equivalent behavior remain unproven. A bounded proof would need the actual target's identity and representation, load ordering, repeated load and unload, multiplayer behavior, and each required scene lifecycle effect. The implementation language remains open.
