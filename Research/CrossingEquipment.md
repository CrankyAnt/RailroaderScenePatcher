# Additional crossing equipment

This report serves [Test additional crossing equipment with existing game models](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/22) and the remaining [crossing boundary decision](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/19). It covers a retained combined light and gate model and a separately constructed bell emitter at one existing protected crossing. It does not establish a generic prefab catalogue or production patch format.

## Build and fixture

The tested installation is Railroader 0.6.2, build 26335745. `Railroader_Data/Managed/Railroader.dll` SHA-256 is `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. The [source inspection](CrossingEquipmentSources.md) identifies the internal construction and consumer paths. The probe is independently authored and built against installed managed references without copying game code, binaries or assets into this branch.

Only [CrossingEquipmentFixture](CrossingEquipmentFixture/Definitions.json) and [CrossingEquipmentProbe](CrossingEquipmentProbe/CrossingExpansionProbe.cs) are active in the isolated test home. The fixture has empty native map data and layers on `rr/bushnell-whittier` with terrain `rr/nc-bryson`. It adds no track, span or global inventory mutation. A new ordinary sandbox, `CrankyAnt Equipment Research`, uses `crankyant/crossing-expansion-research`. The exact target is `World/Large Scenery/Bryson/Grade Crossing - Bryson Everett`.

The process command line and new startup log identify the isolated home. Steam appends another home argument naming the normal profile, but the startup log explicitly identifies the test home and its loaded research packs. No normal save, personal preference file, installed mod or persistent Steam launch setting is changed by this trial. Full startup logs remain local because they contain player identifiers.

## Declared source and ownership

The source is the first complete outer `Signal 1` beneath the target controller. Its inner `Signal 1` has a `Track.KeyValueLampMaterialController` with key `lights`. Its `Crossing Signal Wishbone A (1)` model has an `Animator`, `RollingStock.Controls.KeyValueBoolAnimator` with key `gates`, shared `Scene` clip, and `Gate` transform. The second original device is the controller's outer `Signal 2`. Both source and clone resolve the same nearest parent `KeyValueObject` on the intended controller. There is no nearer owner in the source hierarchy.

The probe creates an inactive owned wrapper beneath that controller, instantiates the complete first device beneath it and places the copy five world units along positive X from the source. It verifies distinct components, remapped lamp target, retained clip asset and the expected hierarchy before enabling. Deferred restore initialization creates both clone observers after activation. A mesh alone would not supply this behavior.

The examined device has zero descendant colliders, as does its copy. This result supplies no collision capability. The controller's existing culler lists exactly the two original roots. This probe preserves that list and does not register the clone with it. The copied device therefore does not inherit the original list's distance-culling behavior. Production visibility and lifetime policy remain separate requirements.

The bell has a different construction path. The controller creates its original `Audio.IntegerLoopingPlayer`; the copied signal contains no bell player. [BellResearch](CrossingEquipmentProbe/BellResearch.cs) creates a second player beneath its own initially inactive child of the same controller. It resolves the existing `rr/bell-uss-teardrop-crossing-rh` definition for indexes and volume, uses the original controller's loaded audio asset, and configures its own player and split clips. Mixer group, priority, distance and randomization settings come from the original player. The original player and its private clip array are preserved.

The extra player's `play` value is explicitly driven from the intended controller's `SignalBell` on each frame. This bounded internal-property binding demonstrates an available route, not automatic native discovery of another bell. It depends on the original controller and its loaded asset reference staying alive. It is not an independently acquired asset lifetime. Removal must precede release of that retained source asset. The static report describes the available key-value subscription and separate asset-loading routes; neither is claimed as tested production behavior here.

## Construction and removal observations

The first clean world constructed the signal copy with remapped targets and active lamp and gate observers. Explicit removal deactivated and destroyed only the owned wrapper. After deferred destruction, verification found no wrapper and retained original source identity, root pose, scale, clip, target references, collider state, culler references and six span objects.

After that verification, the clean sandbox and placed locomotive were saved. A full process restart loaded the same map from that clean save. The same declared controller/source path and offset constructed the device again, with both observers active. This reproduces runtime construction in a fresh world instance without persisting the added object or modifying the native map. The separate bell player also configured successfully in this instance.

The [runtime observations and retained probe log](CrossingEquipmentObservations.md) record a train passage on the original track. The live observation confirmed that all three installations responded identically in visible lamps and gate animation, including the existing first-activation anomaly already reproduced on the base map. Bell sound was audible. At every recorded equipment sample, the source and clone had the same printed lamp colors and gate quaternion. Separate audio samples showed actual playback and advancing time on both the original and added players, followed by release. The added emitter's audibility was not isolated from the original on listening alone.

After the locomotive stopped beyond the crossing, both additions were removed and verified absent after deferred destruction. The checked source, culler and span identities remained intact, as did the original bell player, loaded asset reference and clip array. This does not inspect disposed observers, all indirect references or every affected-state field. No circuit collection was mutated by the equipment probe, but it did not take a structural snapshot of every field within the original sets.

A further world unload/reload from the clean saved sandbox reproduced both additions from the same paths, definition and offsets, with newly initialized observers and inactive outputs. Both were removed and verified again. This tests construction after world replacement, not another train passage, automatic patch application, persisted added objects or controller-disable safety. The active packs and new sandbox were then moved into the inactive case library; the test home's active Mods and ModDev folders were empty and its mod activation lists were cleared.

The result is affirmative for this bounded existing-crossing extension. One retained combined lamp/gate hierarchy and a separate controller-bound bell emitter can be added, exercised, removed and reconstructed without altering the checked original source references. It does not establish other model families or production lifecycle guarantees.

## Decision consequences

- The concrete source is a retained complete scene device, with a separately identified destination controller. A general native crossing prefab route has not been established. Explicit source identity, expected component structure, external clip and material dependencies, controller identity and owned addition identity are material authoring inputs.
- Lamp and gate addressing follows hierarchy and keys. Bell addressing requires an explicit binding. A successful combined model does not establish standalone lights, other gates, other crossings, substitute meshes or a new protected crossing.
- Fail before activation when source or destination identity is ambiguous, required model references differ, a nearer owner intercepts addressing, or an asset is unavailable. Clean up owned additions after a partial failure. This probe's checks are bounded evidence, not a complete transaction or conflict policy.
- Build-specific internal symbols, source hierarchy and assets constrain compatibility. The assembly hash alone does not fingerprint scene bundles. Asset identity, dependency ownership, culling, broader origin/lifecycle behavior and restoration guarantees must be reflected in the downstream decisions where material.
- Existing-operation and broader lifecycle/restoration investigations remain with [Establish missing evidence for applicable scene-object operations](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/18). This report does not reopen or replace those questions. New detection and circuit registration remain with [Test additional crossing detection and controller membership](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/21).

## Reproduction

Build from repository root with an installed .NET SDK and the installed managed references. Keep build outputs outside the repository.

```powershell
dotnet build Research/CrossingEquipmentProbe/CrossingExpansionProbe.csproj `
  -p:GameManaged=D:/SteamLibrary/steamapps/common/Railroader/Railroader_Data/Managed `
  -p:BaseIntermediateOutputPath=C:/TMP/RailroaderMapResearch/Issue22Build/obj/ `
  -p:OutputPath=C:/TMP/RailroaderMapResearch/Issue22Build/bin/
```

Install only the authored probe DLL and its `Info.json` in the isolated home's Mods folder and copy the fixture to its ModDev folder. Explicitly enable only those packs. The fixed mutation commands also require `ARM_EQUIPMENT` in that probe directory and the exact isolated home argument. DLL changes require a full process restart. A `command.txt` file accepts only the fixed commands in the host. Use `add`, `belladd`, `sample` and `bellsample`; removal uses `remove`, `verify`, `bellremove` and `bellverify`, with a frame between each removal and its verification. Do not hand-save while additions are present. Return active packs and activation flags to the neutral baseline after the trial.
