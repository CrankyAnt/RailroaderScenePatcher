# Final editor and driving observations

These observations supplement the captured inventory logs. Game examined: Railroader 0.6.2 experimental. The editor and save tests used the disposable inventorymod rather than a normal save.

## Isolated whistle repeat

Segment Shuo still exists. Its endpoints Nmzo and N51l were moved 20 m along X. Segment Sozu is a different segment deleted for the milepost experiment. The visible whistle board remained at its original position although its Shuo reference still resolved without a marker warning.

The first movement also changed the adjoining Serb and Sou5 geometry and caused a switch geometry error at N8xf. The repeat supplied new fixed nodes at the original endpoint positions to those adjoining segments. Shifted Shuo retained its ID and became isolated. Reload loaded 1868 nodes and 1897 segments with no N8xf geometry error and no warning naming M5pp/Shuo. Seven expected missing-marker warnings from the other deletion tests remained. See [the repeat report](Scope/Tests/07-Inventory/IsolatedWhistleResult.md) and [captured reload](Scope/Tests/07-Inventory/IsolatedWhistleReload.log).

The repeat is a diagnostic layout with gaps, not proof of a playable rework or a repair implementation.

## Retained bridge and independent location board

With track segment 31lvmw selected, the original Ela steel bridge and concrete piers remained beside the moved track. The generated track bridge moved with the graph. The later probe snapshot records the adjusted track used for the driving experiment.

The standalone SYLVA INTERCHANGE board remained at its old position with a new isolated track passing through it. Its inspected hierarchy has no TrackMarker or TrackRelativePosition. Component evidence is recorded in [SylvaBoardScene.json](Scope/SylvaBoardScene.json).

## Driving results

The operator reported that the train passed through the tested Ela bridge piers without a physical collision. The operator also reported passing the standalone location board and the intersecting track without a noticeable collision or derailment. A supplied screenshot showed the train on the test spur crossing the existing track; the screenshot is not distributed in this archive and is not independent proof of every driving observation.

These results concern the specific objects and route tested. They do not establish that every scene object lacks collision or that a physical move or deactivation is safe.

## Editor definition versus save loading

The existing base-map sandbox initially loaded the base graph despite the inventorymod being enabled. Its editor Map definition alone did not merge the delta into that save. Adding the small native [Data.rl loader](InventoryModSnapshot/Scripts/Data.rl), calling map.merge(json.load("Data/Map.json")), applied the map delta during game loading. The subsequent driving observations used that configuration.

This is native map-data loading. It does not add a JSON interface for existing Unity scene objects.
