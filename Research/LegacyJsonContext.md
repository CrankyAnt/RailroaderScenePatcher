# Legacy and native JSON distinction

The local StrangeCustoms documentation describes a separate graph-patching system. Graph.md registers `mixintos.game-graph` files and says StrangeCustoms supports the graph data it dumps. StrangeCustoms.md describes serializing the base-game graph, applying each registered JSON patch, then creating nodes, segments, spans, industries, signs, scenery and splineys from the merged result. JsonPatches.md documents separate instruction objects such as `$find`, `$remove` and `$replace` consumed by StrangeCustoms.

These files explain why a legacy map mod could consist of JSON while still rely on a shared DLL-based implementation. They do not establish whether native Railroader 0.6 supports equivalent operations. That requires checking the new owning reader and current mod examples.

Primary official migration documentation states that pre-0.6 map mods worked through third-party DLL mods reading JSON and constructing track/scenery at runtime, and that 0.6 replaces this with a first-party format:
https://docs.railroadergame.com/modding/migrating/#map-mods

Local historical references inspected:
- Modding/StrangeCustoms/Graph.md
- Modding/StrangeCustoms/StrangeCustoms.md
- Modding/StrangeCustoms/JsonPatches.md

No game, mod, or save files were changed during this investigation.
