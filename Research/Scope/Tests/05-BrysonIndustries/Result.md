# Bryson City Industries standalone test

Game 0.6.2 (26335745), only nikii/br-textile 1.0.0 enabled. Fresh process loading existing MapModScopeSandbox, no editor. Data.rl modifyMap executed and the map loaded successfully.

Six Ops validation errors reference three undefined loads: scold/textiles, scold/yarn and scold/wool. Industry initialization then fails with a NullReferenceException, and servicing the textile loader, two unloaders and formula also throws NullReferenceException.

The installed package's Map.json uses these load IDs. Its Definitions.json defines only a Map, not the referenced Load definitions, and Info.json declares only rr/game >=0.6.0 as a required dependency. The local package therefore does not provide or declare a provider for these loads. Whether the intended correction is to bundle definitions, declare another dependency or change the IDs needs author/source confirmation; no correction was guessed or applied.

No TrackMarker or invalid span diagnostics appeared at capture. These are missing load-definition/configuration problems, not evidence of a new scene-reference patch family. The known base-game store_5_phys diagnostic remains present. Service errors can repeat over time, so counts are capture-specific.
