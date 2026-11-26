<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene patch file v1

This reference defines the JSON fields in format version 1. Each supplying mod keeps one scene patch file at `Data/scene-patches.json`. The file must use strict UTF-8 and may have a leading byte-order mark. The runtime only reads the file. In the native map editor, **Save** writes it for the edited mod. See [Write a scene patch file](writing-scene-patch-files.md) to edit it by hand, or [the modder manual](modder-manual.md) to edit in Railroader.

Current native target support covers:

- Location signs ([move a location sign with its terrain effects](moving-location-signs.md)).
- The Ela bridge and its colliders ([move or disable a bridge with its collision](placing-bridges.md)).
- Groups ([move or disable a group and its parts](moving-scene-objects.md)).
- Parts ([move or disable a single part](moving-scene-objects.md#move-or-disable-a-single-part)).
- Generic track markers ([repair, replace, or detach a track-marker binding](repairing-track-bindings.md) and [give a marker a replacement ID](repairing-duplicate-marker-ids.md)).
- Crossing pads ([rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md)).
- Mileposts ([reposition a milepost](repositioning-mileposts.md)).
- Rail bridge construction controllers ([repair construction segments](repairing-construction-segments.md)).
- Crossing coverage ([correct the detection coverage of an existing crossing](correcting-crossing-coverage.md) and [add detection for extra tracks at an existing crossing](adding-crossing-detection.md)).
- Crossing lights and gates ([add lights and gates to an existing crossing](adding-crossing-equipment.md)).
- Crossing bells ([add a bell to an existing crossing](adding-crossing-bells.md)).
- Existing crossing masts and bells ([move or disable an existing crossing mast or bell](moving-crossing-equipment.md)).
- Starter placements ([place a starter placement marker on the track](placing-starter-markers.md)).
- Player spawn points ([move player spawn points](moving-spawn-points.md)).
- Station map icons ([move or disable a station map icon](moving-station-map-icons.md)).

The Current native targets section describes what has been observed in Railroader and the limits of those observations. The runtime capability classification remains `Unchecked`.

## Scene patch file fields

| Field | Type | Requirement |
| --- | --- | --- |
| `formatVersion` | integer | Required. Must be `1`. The engine reads this before any other content. If the version is missing, is not the number `1`, or is newer, none of the file's modifications applies and the file is protected from overwrite, regardless of its other content. |
| `sourceMap` | object | Required. Must contain `mapId`. |
| `sourceMap.mapId` | string | Required and non-empty. It must identify the selected map definition or one of its resolved map layers. |
| `modifications` | object | Required. An object keyed by modification ID. Each key is non-empty and not only whitespace, and each value is an object. |

The parser rejects the whole scene patch file if a property name repeats in any object, at any depth. This includes repeated modification keys, circuit IDs, and addition IDs.

The engine applies a scene patch file only when `sourceMap.mapId` matches the identifier of the selected map definition or a resolved map layer. Otherwise, it rejects the file before resolving targets or making changes. In a save, the game skips a file that names a map patch, because a map patch is never the loaded map or one of its layers. The native map editor also refuses that file, even when you are editing the map patch. Its diagnosis names the bottom map layer to use instead.

When Save creates a scene patch file, it sets the scene target map to the edited map definition's bottom map layer. This is the lowest layer the game composes for the definition, including layers inherited through other layers. If the definition has no map layers, Save uses the definition itself. When you edit a map patch, Save uses its bottom map layer, never the map patch itself.

For example, a map definition that builds on `rr/bushnell-whittier` gets a file that names `rr/bushnell-whittier`. That file applies to a save of the base-game map, where the mod's native changes arrive through its map-changing script or map patch even though the mod's own map definition is not loaded. It also applies to every layered map that builds on the base-game map, including the mod's own map.

The patcher keeps an existing file's scene target map, even on Save. To apply a file only on your own map, set `sourceMap.mapId` to your map definition's identifier by hand. **Copy ID** copies the scene target map from the edited mod's file, or the scene target map that Save would write if the file does not exist yet. If the file cannot be read, **Copy ID** is unavailable and its tooltip gives the reason.

## Modification fields

The ID of a modification is its key in `modifications`. A modification has no `id` member. If it contains one, it fails as unsupported content. The diagnosis names the modification's key and explains that the key is its ID. The engine preserves the modification and still applies the others.

| Field | Type | Requirement |
| --- | --- | --- |
| `target` | object | Required. Must contain `scene`, `path`, and `component`. Optional `structure` is a string. Optional `padMarkerId` is a non-empty string that names a crossbuck whose path other crossbucks share; see [Group](#scene-object). |
| `markerId` | string | Optional. The replacement ID of a generic track marker. Requires patcher 0.5.0 or later; see Replacement ID below. |
| `placement` | object | Optional. A fixed placement contains `mode`, `position`, and `rotation`; a track-following placement contains `mode`, `track`, `offset`, and `rotation`. See Placement below. |
| `active` | boolean | Optional. `false` disables the object, `true` enables it. Generic activity requires patcher 0.2.0 or later; track-marker activity requires 0.5.0 or later. |
| `relationships` | object | Optional. Explicit relationship settings; see Relationships below. Requires patcher 0.5.0 or later. |
| `starterPlacement` | object | Optional. Sets a starter placement marker's track location, whether its starting cars are a wreck, or both. See Starter placement below. Requires patcher 0.5.0 or later. |
| `dependsOn` | array of strings | Required. Each value names another modification in the same scene patch file. |

The `target` object accepts only `scene`, `path`, `component`, and the optional members `structure` and `padMarkerId`. All required target strings must be non-empty, as must `padMarkerId` when present. `path` is always the object's hierarchy path. Use `padMarkerId` alongside it only for a crossbuck that shares its path with other crossbucks. For every other target, the patcher refuses `padMarkerId` before changing anything and gives the reason. The scene adapter must resolve the identity to exactly one object. If it finds no match or multiple matches, the modification fails. With no match, the Log window row reads `This target doesn't match any object in the loaded map. Inspect the intended object in the Scene Patcher. On the Object tab, compare its Identity fields with the target's scene, path, and component in the scene patch file. Correct the target or remove this modification from the file.` With multiple matches, it reads `This target matches more than one object in the loaded map. Use a more specific path or component so the target matches only one object.`

The structure IDs `scene-object-v1`, `child-object-v1` and `location-sign-v1` replace `baked-scenery-group-v1`, `baked-scenery-part-v1` and `independent-board-v1`. The old IDs are not read. A modification whose target carries an old ID matches no object and fails with the Log row `This target doesn't match any object in the loaded map.`; the other modifications of the file apply, and the file is not rewritten.

The order of keys in `modifications`, members of any object, and values of `dependsOn` does not affect application. The engine applies each modification after all modifications named in its `dependsOn`. Where `dependsOn` specifies no order, the engine uses the ordinal order of the modification keys, defined under Canonical order below. The result is therefore independent of the file's text order. A repeated `dependsOn` value counts once. If a dependency fails, the engine skips the modification that depends on it. Modifications that don't depend on it still apply.

In the native map editor, an edit cannot remove a modification while another modification names it in `dependsOn`. The refusal names those modifications. You also cannot edit a modification that does not take effect because a dependency is missing, fails, or forms a cycle. The refusal gives the reason.

A modification holds the desired state of one object. It must contain at least one of `markerId`, `placement`, `active`, `relationships`, or `starterPlacement`. An omitted member leaves that part of the object untouched by this mod. The patcher applies an explicit `active` value every time it applies the file. If the game later activates an object that the file disables, the next application disables it again. Patcher 0.1.0 rejects a modification that contains `active`. The dependency minimum below prevents that combination.

The [scene object inventory](inspect-scene-objects.md) supplies observed identifiers and placement without changing this format. An inspection-only entry does not enable a new v1 operation. The engine rechecks the resolved object and map context immediately before changing anything. A missing, ambiguous, or replaced target fails rather than redirecting the operation. Editing integrations can bind a complete scene patch file edit to its inspected observations through an in-memory token; that token is not serialized or distributed.

[World selection](select-world-targets.md) confirms scene-object identities for the existing `target` fields. Segment and node choices record their native IDs and loaded map context in the editing session. Version 1 stores fixed and track-following placement, activity, track-marker relationships, and crossing coverage. A pending choice or confirmed track or node identity stays in the editing session. Applying an edit and using the native map editor's Save writes the mod values.

## Replacement ID

`markerId` gives a generic track marker a replacement ID. The game registers a track marker's location under its marker ID. If more than one scene marker holds that ID, it is a duplicate marker ID. A change to one holder removes every holder's registration. A replacement ID gives the target its own ID, so its registration no longer uses the ID shared by the other holders.

`markerId` must be a string. Omit it to keep the marker's ID from the loaded map. There is no detach form. `null` and any other non-string value fail the modification.

`markerId` applies only to a generic track marker with the target structure `track-marker-v1`.

Any other target, such as a crossing pad, a milepost, a starter placement marker, or an object that is not a marker, fails the modification before it changes anything, with `Only objects in the Generic track marker family can have a replacement ID.` The object's other modifications still apply.

The value must follow these rules, which the engine checks before it changes anything:

- It is not empty or only whitespace, it does not begin or end with a space, and it does not begin with `passengerStop:` or `flare:`. The game uses these prefixes for its own track registrations.
- It is not in use when the modification applies. The modification fails if another scene marker holds the value, a graph registration holds it for an object that is not a scene marker, or a map object in the map context uses it. The refusal names the object using it. The check reads the world at the time of application. Until the game loads the map object manager, every value fails the modification with a message asking you to try again after the map has loaded. If an earlier scene patch file in load order has already given the value to a marker, the later modification fails and the earlier one stays applied.
- The same value in two modifications of one scene patch file fails both of them with `is used by more than one modification`. The value names one marker in the file.

The replacement ID applies before the marker's `relationships.trackMarker`, its track-following placement, and its `active` value, in the same modification. One modification can therefore give the marker an ID and then enable, rebind, or track-follow it. Without the ID, the duplicate marker ID refuses those settings for every holder. Fixed `placement` is not affected by the duplicate marker ID.

If other holders take replacement IDs in the same file, a modification for the holder that keeps the shared ID needs `dependsOn` when it sets activity, `relationships.trackMarker`, or track-following placement. It must name the keys of the other holders' replacement-ID modifications so the patcher applies those IDs first. Without these dependencies, the change can run while the ID is still shared and be refused because of the duplicate ID. The editor writes these keys for you. Include them yourself in a hand-written file. While a dependent modification remains, you can't remove a modification it names in `dependsOn` or clear that modification's replacement ID. For example, when `repair-yard-limit` gives the yard limit its replacement ID, the modification of the speed sign that keeps `Mg6m` reads:

```json
"keep-speed-sign-on-track": {
  "target": {
    "scene": "<scene of the speed sign>",
    "path": "<path of the speed sign>",
    "component": "Track.TrackMarker",
    "structure": "track-marker-v1"
  },
  "relationships": {
    "trackMarker": { "segment": "pi3pzp", "end": "a", "distance": 3 }
  },
  "dependsOn": ["repair-yard-limit"]
}
```

When the marker is active and its binding resolves, applying the ID moves its registration to the new ID. Every other holder of the old ID keeps its registration. If the modification fails, or you remove the modification or the member, the patcher restores the effective underlying ID and its registration. It computes that ID from the loaded map and the file's remaining values.

The member requires patcher 0.5.0 or later; see [dependency minimum](#dependency-minimum). Format version 1 does not change. The engine does not check Lua scripts that look a marker up by ID, such as those that call `NativeTrack.GetMarkerLocation`, or third-party mods that look a marker up by ID. In Railroader 0.6.3, a repaired marker registers under its replacement ID while the other holders keep their registrations, including after Save and a world reload. For the editing steps, see [repair a duplicate marker ID](repairing-duplicate-marker-ids.md).

## Placement

`mode` selects the placement mode. Any value other than the modes below makes the modification unsupported. The engine preserves the modification without executing it. Each target type defines what the optional `structure` means, as described under Current native targets.

Placement and logical binding are separate. Placing an object never changes its track relationships, and changing a relationship never moves the object. A track-following placement keeps its track reference inside `placement`, never in `relationships`. Changing either reference leaves the other unchanged.

A fixed placement accepts only `mode`, `position`, and `rotation`. `mode` is `fixed`. `position` must contain three finite JSON numbers in map coordinates. `rotation` must contain four finite JSON numbers, representing a nonzero quaternion. The location sign and bridge adapters require a unit quaternion and perform the game map/world coordinate conversion.

A track-following placement accepts only these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `mode` | string | `trackFollowing`. |
| `track` | object | Required. A segment location with exactly `segment`, `end`, and `distance`, with the same rules as a `trackMarker` reference, or an endpoint with exactly `node`, a non-empty track-node ID. |
| `offset` | array | Required. Three finite numbers in meters, in the track's frame. |
| `rotation` | array | Required. Four finite numbers, a nonzero quaternion applied after the track's rotation. |

When it applies the modification, the engine reads the track's native position and rotation in map coordinates. For a segment location, it reads them from the track graph at `distance` meters from the named end. The frame's forward axis (+z) points along the segment away from that end. For `end` `a`, it points toward end `b`; for `end` `b`, it points toward end `a`. For an endpoint, the frame uses the node's native position and rotation.

The object's position is the track position plus `offset` rotated by the track rotation. Its rotation is the track rotation followed by `rotation`. Applying the same scene patch file again produces the same placement. Offsets don't accumulate.

If the segment or node is missing, or the distance is outside the segment, the modification fails before changing anything and the object keeps its placement. The engine never substitutes another track. Each application evaluates the track's current pose. A world load after the track moves therefore places the object at its new position relative to the track, without changing the stored `offset` or `rotation`.

In the native map editor only the edited mod's scene patch file applies. The patcher also moves its following objects while their track moves; see [follow a track or endpoint](following-track.md). In ordinary play, every applicable scene patch file evaluates the track's current pose at world load.

## Relationships

The `relationships` object accepts `trackMarker`, `crossingCoverage`, `crossingEquipment`, and `constructionSegments`, and must contain at least one of them. Each relationship has three settings:

| Setting | Meaning |
| --- | --- |
| Omitted | The relationship stays as it is. This mod does not touch it. |
| A reference | Sets or replaces the relationship. |
| `{ "detach": true }` | Clears the relationship. |

The construction segments of a rail bridge cannot be detached; see [construction segments](#construction-segments).

A `trackMarker` reference is an object with exactly these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `segment` | string | Required and non-empty. The ID of a track segment in the loaded track graph. |
| `end` | string | Required. `a` or `b`: the segment end the distance is measured from. |
| `distance` | number | Required. Finite, not negative, and at most the segment's length, in meters. |

A detach object contains only `detach` with the value `true`. `null`, `{}`, `false`, and a detach mixed with reference fields are invalid. Unknown relationship kinds and unknown fields inside `trackMarker` make the modification unsupported; the engine preserves and does not execute it. The target must resolve to an object whose adapter supports the relationship. The patcher refuses a relationship on any other object instead of ignoring it.

The engine checks the reference before changing anything. A segment that does not exist, a segment ID that matches more than one segment, or a distance outside the segment fails the modification, and the object keeps its relationship and registration. The engine never substitutes another target.

### Crossing coverage

`crossingCoverage` requires patcher 0.5.0 or later. It is an object that holds `circuits`, `additions`, or both:

| Field | Type | Requirement |
| --- | --- | --- |
| `circuits` | object | Optional. Keyed by circuit ID: the ID of the circuit's island span from the map, which must name a circuit of the target crossing. Holds one entry per explicitly set circuit, and at least one. |
| `additions` | object | Optional. Keyed by addition ID. Holds one entry per detection addition, described under Detection additions below, and at least one. |

An entry under `circuits` contains either the three spans or a detach, and no member that names the circuit:

| Field | Type | Requirement |
| --- | --- | --- |
| `approachA`, `island`, `approachB` | object | Required together, unless the entry detaches. Each holds `from` and `to`, both track locations with the `segment`, `end`, and `distance` fields above. |
| `detach` | boolean | `true`, alone in the entry. The crossing stops evaluating the circuit. |

The three settings apply per circuit. An omitted circuit keeps its effective coverage, and omitting `crossingCoverage` leaves every circuit untouched. Selecting tracks replaces the circuit's spans with owned spans that cover the route from `from` to `to`. Here, `from` is the native lower end, which lies next to the island for each approach. Detaching a circuit stops the crossing from evaluating it. In both cases, the map's own detection spans for the circuit stay registered and unchanged. A still-valid old route therefore stops triggering the crossing without being removed. Circuits keep their identity and native order.

The modification fails as an invalid definition if `crossingCoverage`, `circuits`, or `additions` is empty or isn't an object, or if a circuit or addition key is empty or only whitespace. The engine preserves the modification and still applies the others. A repeated key inside `circuits` or `additions` is a repeated property name, which rejects the whole scene patch file as described under Scene patch file fields above.

Before changing anything, the engine checks every entry. The circuit must exist on the crossing, each segment must exist exactly once, each distance must lie inside its segment, and no span may have zero length. At least one circuit must keep detection. The engine refuses a modification if its result, together with the crossing's current effective coverage, detaches every circuit. A refusal leaves the crossing's coverage unchanged.

Unknown fields in `crossingCoverage` or an entry make the modification unsupported. An entry with `detach` and span fields, `"detach": false`, an entry that is not an object, and a partial set of spans are invalid.

#### Detection additions

A detection addition makes the target crossing detect explicitly selected extra tracks. The patcher builds and owns its circuit alongside the crossing's native circuits. An entry under `additions` is keyed by the addition's ID, which is unique in the scene patch file and scoped to the supplying mod. The entry contains exactly these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `approachA`, `island`, `approachB` | object | All required. Each holds `from` and `to`, both track locations with the `segment`, `end`, and `distance` fields above. |

The ID is the addition's persistent identity. The patcher combines it with the supplying mod's identifier. Two mods using the same ID therefore get separate additions. Each application of a scene patch file finds the addition it built before. An ID may appear in only one modification per scene patch file. If two modifications use it, both fail with that reason, regardless of their order in the file.

The three spans define the addition's tracks and detection area. The named segments are the tracks that get detection, and the distances bound the area. The engine does not infer either from track near the crossing. An entry under `additions` never contains `detach`. The addition exists while the scene patch file lists it; removing the entry deletes it.

Before changing anything, the engine checks an addition's selected tracks. Each segment must exist exactly once, each distance must lie inside its segment, and no span may have zero length. An addition counts toward the requirement that a crossing keep at least one detecting circuit. The engine refuses a scene patch file result that leaves no detecting circuit or addition.

For each addition, the engine builds one circuit with three owned spans, registers the spans in the track graph, and adds the circuit after the controller's native circuits. Added circuits are ordered by their scoped identity. Within one mod, this is the ordinal order of their IDs, regardless of their order in the file. A repeated application reuses the circuit and every span whose tracks are unchanged. It replaces the spans for any role whose tracks changed. Deleting an addition removes its circuit from the controller, unregisters its spans, and destroys them. Native circuits and other additions keep their state.

### Crossing equipment

`crossingEquipment` lists the lights, gates, and bells that the supplying mod adds to the target crossing. It requires patcher 0.5.0 or later. Each addition belongs to the controller targeted by the modification, and that controller drives it.

The value is an object keyed by equipment ID, with at least one entry. Each key is the addition's persistent ID and contains 1 to 64 letters, digits, `.`, `_`, or `-`. IDs are scoped to the supplying mod, so another mod can use the same ID for a different addition. Within one scene patch file, an equipment ID may appear in only one modification. If two modifications use it, both fail with a message explaining the conflict. Each entry has the following members and no `id` member:

| Field | Type | Requirement |
| --- | --- | --- |
| `pattern` | string | Required. The source must meet the requirements of this supported source pattern. Version 1 supports `crossing-light-gate-v1` for a light or gate and `crossing-bell-v1` for a bell. |
| `source` | object | Required. For `crossing-light-gate-v1`, use `scene` and `path` to name the Unity scene and hierarchy path of a device loaded in the map. Both must be non-empty strings. For `crossing-bell-v1`, use only `definition`. It must be a non-empty string identifying a bell definition that the game uses at grade crossings, such as `rr/bell-uss-teardrop-crossing-rh`. |
| `placement` | object | Required. The fixed placement of the copy's root or the bell, with `mode` set to `fixed`, and `position` and `rotation` in map coordinates. See Placement. |

Applying a scene patch file leaves unlisted additions untouched, including those of other mods. When you edit or remove an addition in the edited mod's scene patch file, the patcher removes any addition that file no longer lists. The next load does not rebuild it. Repeated application keeps one copy or bell per scoped ID, updates its placement if needed, and rebuilds it if its `source` or `pattern` changes. Each bell keeps one player, one observer of the bell key, and one loaded audio reference.

The modification fails as an invalid definition in any of these cases:

- `crossingEquipment` is empty or isn't an object.
- A key is empty or invalid, an entry isn't an object, or a pattern is missing.
- A light or gate source lacks a scene or path, or includes `definition`.
- A bell source contains anything other than `definition` with a non-empty string.
- A placement isn't fixed.

A repeated key is a repeated property name, so the patcher rejects the whole scene patch file. An unknown pattern or an unknown member in an entry, its `source`, or its `placement` makes the modification unsupported. This includes an `id` member. The patcher preserves unsupported modifications without executing them.

Before changing anything, the engine checks every addition in the modification. Its pattern must be supported. Its source must resolve to exactly one loaded object or one bell definition in the content library. The source must also meet the pattern's requirements for the target controller. See [ineligible sources](adding-crossing-equipment.md#ineligible-sources) and [ineligible bell definitions](adding-crossing-bells.md#ineligible-bell-definitions). A missing or ambiguous source fails the modification. This includes an object in an unloaded scene or a definition absent from the content library. The engine never loads a scene or substitutes another source or sound.

`crossing-light-gate-v1` has these requirements:

- Each lamp (`Track.KeyValueLampMaterialController`) and gate (`RollingStock.Controls.KeyValueBoolAnimator`) in the source observes the key the controller publishes as its lights or gates key.
- Lamp targets lie inside the source and use the lamp material. Each gate has an `Animator` and a clip.
- The source has at least one lamp or gate. It has no crossing controller, key-value object, track span, culler, bell player, or missing script.
- The source is in the destination controller's Unity scene. The destination controller has exactly one key-value object and at most one culler.

`crossing-bell-v1` requires a definition of kind `Bell` in the content library that the game uses at grade crossings. In the base game, `rr/bell-uss-teardrop-crossing-rh` is the only such definition. Locomotive bells and bell definitions added by other mods are refused. The crossing bell's audio asset must resolve in that library, its volume must be a number greater than 0, and its audio indexes must be times greater than 0 in ascending order. The destination controller must have exactly one key-value object and be active.

Audio loads after the modification has been applied. Once it loads, the indexes must divide the clip into parts of at least one sample each. If loading fails or the indexes do not fit the clip, the bell remains built but silent. The game's log shows one warning under the supplying mod's scene patch file, with the bell's scope, ID, controller, and reason. The bell then fails its check. The patcher refuses edits that keep it, but you can still remove or replace it.

While any addition at the target crossing fails its check, the patcher refuses equipment modifications before making a change. A modification that leaves the crossing's equipment as it is, such as a coverage change, neither checks nor touches the additions.

[Explicit snapping](snapping-to-track.md) writes a fixed placement. The editor computes a point on the explicitly selected segment's cubic curve or uses the selected node's position, preserving rotation, activity, and logical binding. The graph selection and geometry checks belong to editing preflight and are not serialized. Runtime application and reload use the resulting absolute placement without querying a snap target. Snapping adds no dependency minimum; later track movement does not change the saved placement. Track-following placement is the separate mode above.

## Starter placement

`starterPlacement` controls where a cut of starting cars appears in a new company game and whether it starts as a wreck. It sets the starter placement marker's track location, the car placement's wreck setting, or both. The track location is where the front of the cut starts. Identify the marker with target `component` `Track.TrackMarker` and `structure` `starter-placement-v1`.

The object accepts only the fields below. It must contain `track`, `wreck`, or both.

| Field | Type | Requirement |
| --- | --- | --- |
| `setup` | string | Required and non-empty. The identifier of the setup whose car placement reads the marker. |
| `track` | object | Optional. The marker's track location. Must contain exactly `segment`, `end`, and `distance`, following the rules for a `trackMarker` reference. The parser accepts `{ "detach": true }`, but the engine refuses to apply it. |
| `wreck` | boolean | Optional. `true` starts the cars derailed, using the game's own wreck behavior. `false` starts them on the track. All other values, including `null`, are invalid. |

The cut extends from the track location toward the segment end from which `distance` is measured. With `end` set to `a`, it extends toward end `a`; with `end` set to `b`, it extends toward end `b`.

Omitting `track` leaves the marker's track location untouched by this mod. Omitting `wreck` keeps the car placement's native wreck setting. An explicit `wreck` value is reapplied every time, even if it equals the native setting. Omitting `starterPlacement` leaves both settings untouched by this mod.

A starter placement marker accepts no `placement`, `active`, or `relationships`. Its object position and activity do not affect where the cars start. Its track location belongs in `starterPlacement`.

Before changing anything, the engine refuses these invalid definitions:

- A track location whose segment is missing or ambiguous. The refusal names the segment.
- A distance outside the segment. The refusal gives the distance and the segment's length.
- A detach, a `setup` other than the setup whose car placement reads the marker, or a marker read by no car placement of an active setup or by more than one. The refusal gives the reason. This also applies to a modification that sets only `wreck`.
- A location where the whole car cut, together with the cuts of the setup's other placements, fails the game's placement rule. The refusal names the car placement, its cars and length, and the game's reason.

The engine does not substitute another segment for a missing, ambiguous, or out-of-range location. For each other cut, it uses the track location set by that cut's starter placement in the file, or its current location if the file sets no location for it. The result therefore does not depend on the order in which modifications apply. See [place a starter placement marker on the track](placing-starter-markers.md#what-the-patcher-checks-first). A refused modification leaves the marker and the wreck setting as they were. The other modifications still apply.

If an application fails after changes have begun, the engine restores the captured wreck setting, track location, and registration. Removing the modification or its `wreck` member restores the wreck setting the car placement has without this mod, recalculated from the loaded map and remaining values.

## Objects the game switches

Besides track-group activation, the game uses these controllers to switch some listed objects on and off in Railroader 0.6.3:

- A progression controller (`Game.Progression.ProgressionEntityObjectController`) on a place's scene object below `World/Large Scenery`. It sets the objects of its lists from the progression state of its industry or passenger stop, when it starts and whenever the industries change. It switches 13 station map icons, the station groups `Cochran flagstopstation`, `Alarka Depot`, and `Alarka Jct Station`, and the track marker on `Hemingway flagstopstation`.
- A map feature (`Game.Progression.MapFeature`). It sets the objects of its `gameObjectsEnableOnUnlock` list from whether it is unlocked. The `signals` map feature switches the track markers `Sign Begin CTC Andrews` and `Sign Begin CTC Sylva`.
- A rail bridge's construction controller, described under [Group](#scene-object).

These rules apply only to objects named directly in a list. Switching an object that holds another object doesn't count as switching that object. For every directly named object, regardless of family:

- `"active": false` is an explicit disable, which holds through the switch. The patcher points each list entry that names the object at a hidden stand-in object, so the game's later switches set the stand-in, and the object stays inactive.
- An omitted `active` leaves the switching to the game, as without the patcher.
- `"active": true`, clearing `active`, and removing the modification return the object to the state that the switch gives it now, and the switch sets the object again from then on. The patcher verifies the `active` value, not the activity the switch chose, so an enabled object that progression keeps off still verifies.
- Undo and Redo of the disable keep the object consistent with the switch's current state.

For a track marker whose activity also follows a track group, `active: false` holds against both its track group and the map feature or progression controller; `active: true` stays refused, as under [Markers with group-driven activity](#markers-with-group-driven-activity).

In the native map editor, the **Object** tab and the footer's message area show an amber warning for an explicitly disabled object that a progression controller or a map feature switches: `Your explicit disable keeps this object off in the game even when progression switches it on. Click Enable or Clear activity to let progression control it again.` for progression, and `Your explicit disable keeps this object off in the game even when unlocking a map feature switches it on. Click Enable or Clear activity to let the map feature control it again.` for a map feature, and `Your explicit disable keeps this object off in the game even when progression or a map feature switches it on. Click Enable or Clear activity to let them control it again.` for an object that both switch. For a track marker whose activity follows a track group, where `active: true` is refused, the warning reads `Click Clear activity` in place of `Click Enable or Clear activity`. An object without an explicit disable, or one that the game does not switch, shows no such warning. While the disable holds, the **Dependencies** section keeps naming the progression controller or map feature that switches the object. While no explicit disable holds the object and the game switches it off, **Enabled** on the **Object** tab reads `Yes, but currently switched off by the game`.

The patcher refuses to disable an object if it couldn't keep it disabled. This applies to station groups and parts, station map icons, track markers including mileposts and crossing pads, and existing crossing masts and bells. The patcher gives the reason before changing anything, whether the disable comes from the native map editor, a hand-written scene patch file, or an import. The modification fails, but the file's other modifications still apply and you can still enable the object.

This refusal occurs in either of these cases:

- Progression or a map feature switches the object, but the patcher cannot share its activity in this load. The reason is `The patcher can't share control of this object's activity with the game's progression or map feature during this load. Disabling it wouldn't keep it off, so it can't be disabled.`
- A progression controller or map feature names the object, but the game's members for that switch are unavailable in the build. The reason is `The required members of the game's progression controller are unavailable. Disabling this object wouldn't keep it off against progression, so it can't be disabled.`, or `The required members of the game's map feature are unavailable. Disabling this object wouldn't keep it off against its map feature, so it can't be disabled.` The Player log also names the missing members.

In the native map editor, **Disable** is then unavailable with the same reason. None of this has been observed in Railroader yet.

## Several mods

During ordinary play, the runtime considers each loaded mod that has `Data/scene-patches.json` and lists `crankyant/scene-patcher` under `dependencies.required` in its `Info.json`. It processes those files in Railroader's load order, the order of the game's loaded mod list. Map-layer order plays no part, and the scene patch file has no priority field. A mod that has the file but doesn't list the patcher gets one warning in the Player log, such as `My mod [me/my-mod]: Data/scene-patches.json wasn't applied because Info.json doesn't list crankyant/scene-patcher under dependencies.required. Report this to the mod author.`

The runtime skips a file whose `sourceMap.mapId` matches neither the selected map definition nor one of its resolved map layers. The patcher is expected to add a game warning naming the supplying mod, its identifier and version, the file `Data/scene-patches.json`, the map the file is for, and the loaded map and its layers. The warning doesn't name an operation or object and doesn't ask for a report, because the whole file is meant for another map. Other scene patch files still apply. Clicking **Editor** in the main menu loads the game's editor default map, `rr/editor-default-map`, before you choose a map. That load adds no such warning.

With **Debug logging** on, the Player log has one debug line per scene patch file for each load attempt, such as `... map rr/bushnell-whittier (layers none): My mod [me/my-mod] scene patch file for rr/bushnell-whittier: 2 of 2 modifications applied, 0 failed, 0 unchanged`, or `... scene patch file for me/my-map skipped: not the loaded map or one of its layers`. A skipped scene patch file keeps its one **Log** window row, which is its only Player log line while **Debug logging** is off. In the native map editor, only the edited mod's scene patch file applies, as described above.

A failed scene patch file or modification does not undo earlier scene patch files or stop later ones. An unreadable scene patch file or `Info.json` skips that mod as a whole.

When scene patch files from different mods modify the same existing target, the runtime adds one informational game message for that object. It names the object as `scene/path [component]`, lists the mods and their identifiers in load order, and states any declared dependency among them. If another mod modifies the same object, the runtime updates the message.

The message reports a possible incompatibility, not a confirmed conflict. Application continues in load order, no change is undone, and the later scene patch file's values apply last. A declared dependency does not suppress the message. There is no message for a target that resolves to no object or several objects, or for modifications from one mod alone. The message is not a failure report and adds no JSON fields.

## Runtime failure reports

If a scene patch file or modification fails at runtime, a structured warning appears in the game, separate from the historical file log. A modification warning identifies its source as Railroader Scene Patcher. It includes the supplying mod's name, identifier and version, `Data/scene-patches.json`, the operation, the target when resolved, the error, and the patcher and game versions. The warning ends by saying once whom to report it to. A warning for the mod author names the supplying mod's author when the mod supplies one, as in `Report this to the mod author: <author>.`, and includes an HTTP or HTTPS website when present. A warning for the patcher maintainer ends with `Report this to the patcher maintainer.` and names no mod author. The warning doesn't invent contact details.

Invalid supplied definitions are reported to the mod author. Unknown failures and demonstrated patcher faults are reported to the patcher maintainer. The runtime reports a given source and operation once per loaded world. Invalid or unsupported scene patch file content that requires recovery remains an editing matter; this reporting behavior adds no JSON fields or format version.

## Current native targets

### Location sign

For a location sign, the target `scene` and `path` name the location sign. Its `component` is `Helpers.Culling.RendererCuller`, and `structure`, when present, must be `location-sign-v1`. The object must carry that culler and exactly one `Map.Runtime.MaskComponents.CircleMapMask`, both enabled, with active parents. A mask that also flattens the ground moves with the location sign. The game rebuilds the terrain at both positions. The patcher refuses a hierarchy with a `Track.*` component, another culler, another mask component, another culling handler, or a missing script. You can disable the location sign itself through `active`. The patcher requires the map conversion, culling, mask, terrain tile, and terrain completion functions it uses. A location sign's placement has no track binding.

After setting the location sign's placement, the patcher requests a culling-position update and rebuilds the circle map mask. It then checks that the culling sphere and every mask registration are at the location sign's position. For a disabled location sign, neither registration may remain. A mismatch fails the modification and restores the captured placement, activity, and registrations.

The game regenerates terrain output at the old and new positions. The patcher reports a position complete once it receives a tile-built notification covering that position and terrain work is idle with no pending invalidations. A position whose terrain tile isn't built needs no rebuild. If a position hasn't completed after 30 seconds, the patcher reports the completed and outstanding positions. The modification stays applied. Save doesn't write the generated terrain output; the game regenerates it from the saved placement. A location sign modification doesn't change native edited terrain tiles.

If a change fails and restoration is verified, later modifications of the same location sign fail until the restored terrain output completes. If that output stops, or restoration cannot be verified, they fail for the rest of the load.

An isolated native test on Railroader 0.6.2 build 26335745 observed the rendered Sylva location sign, updated culling sphere, and registered mask at the desired placement, with settled terrain output. The sampled tree regions were empty. A separate research trial on the same build moved the Sylva location sign's mask onto one generated tree. The tree disappeared from the generated tile output and returned after restoration. Other location signs qualify through the component pattern without individual trials.

The Current native targets section lists all supported object families. Families not listed there are not implemented.

### Bridge

The patcher recognizes the Ela bridge root at `World/Large Scenery/Ela/Ela Bridge`, with target `component` `Helpers.Culling.RendererCuller`. Set `structure` to `bridge-colliders-v1`; the native map editor's Save writes it. Use the `scene` value from the inspected identity. A modification may set `placement`, `active`, or both, as for a location sign.

The patcher moves the bridge root and enables or disables it. Its colliders and renderer cullers move with it. The patcher refreshes the culling position of every active culler in the hierarchy and synchronizes the physics scene.

The parents of the root and its root culler must be active. The hierarchy must contain at least one collider, no rigidbody, and no track-owned component. No construction-scenery entry may switch the root. Before anything changes, the collider identities, shapes, enabled flags, and positions relative to the root must match their first observation in the load.

After a change the patcher verifies the root placement and activity, the collider layout, collider activity under the root, and that the loaded generated trestles within 250 m of the bridge and the construction-scenery controller that references the bridge are unchanged. Separate track-owned scenery is never part of the moved hierarchy. A difference fails the modification and restores the captured bridge state; an unverified restoration blocks further operations on the bridge for the load. See [move or disable a bridge with its collision](placing-bridges.md) for editing instructions and limits.

### Station map icon

The Station map icon family covers each object with a `UI.Map.MapIcon` component directly in `World/Map Labels/Station Icons`. The target `scene` and `path` name the icon's object, its `component` is `UI.Map.MapIcon`, and `structure`, when present, must be `station-map-icon-v1`. The native map editor's Save writes the structure.

An icon elsewhere, or deeper below `World/Map Labels/Station Icons`, doesn't resolve. Neither does an icon that the game creates for a car or player while it runs. The inventory gives each icon its own row and gives the `Station Icons` object no row of its own. A modification may set `placement`, `active`, or both.

The patcher moves the icon's object and enables or disables it. The map draws the icon where its object stands. In Railroader 0.6.3, a moved Bryson icon appeared 300 m east of its original position in play. By the game's code, an icon registers with the map when it is enabled and leaves it when it is disabled, through its own lifecycle (`UI.Map.MapIcon.OnEnable`, `OnDisable`; `UI.Map.MapBuilder.Add`, `Remove`). The station's track span, depot house and station agent are native map data in `Map.json`; no modification of an icon changes them. Before changing anything, the icon's scene must be loaded, its hierarchy must hold no missing script, rigidbody, or culler, and its map coordinates must be readable. A culler refuses only the icon whose hierarchy holds it.

After a change, the patcher verifies the icon's placement and activity, that it stands in the same object as before, and that the map has the icon registered while it is active with its parents; before the map exists, registration is not checked. A difference fails the modification and restores the captured placement and activity; an unverified restoration blocks further operations on the icon for the load. The game's progression switches 13 icons on or off with their station at load and when industries change; `"active": false` holds through that switch, as [objects the game switches](#objects-the-game-switches) describes. See [move or disable a station map icon](moving-station-map-icons.md) for editing instructions and limits.

<a id="scene-object"></a>

### Group

The patcher recognizes objects in the Group family. The target `scene` and `path` name the group, its `component` is `UnityEngine.Transform`, and `structure`, when present, must be `scene-object-v1`. The native map editor's Save writes the structure.

A group is a scene object one level below an object that only holds other groups under `World`. Under `World/Large Scenery`, it is one level below a place's object. `World/Large Scenery/Almond Bridge` is itself a group. It holds one rail bridge construction, with `Abutments` and `Steel` as parts. When an object that only holds other groups holds crossings, or crossings and road bridges, the inventory lists its children by their family or group.

To appear in this family, a group must meet all these conditions:

- The scene serializes it.
- It isn't a `Map.json` object and no other family lists it.
- At or below it, an object saved by the scene holds a renderer, collider, light, storage pile (`Scenery.Pile`) or log pond (`Scenery.LogPond`).

A candidate without one of those components isn't listed at all. A collider built by the game while the camera is near doesn't count. For a storage pile, the pile itself counts, but its generated collider doesn't.

`World/Large Scenery/Robinson Gap Mine/Coal Loader`, which holds a car loader (`RollingStock.CarContentSwapper`), is a decorative group under Industry building. Target the group with `scene-object-v1` and its parts with `child-object-v1`. The game loads Robinson Gap coal by teleport between the industry's native track spans, not at the loader, so neither `placement` nor `active` changes where loading happens.

The family excludes a group that holds a track marker, station click spot, spawn point, or map icon at any depth. It also excludes a group that holds a construction controller (`Game.Mow.TrackConstructionScenery`) but isn't a rail bridge construction. Those groups are inspection only. You can't move a group with a spawn point as a whole. Edit its spawn points individually in the [Spawn point](#spawn-point) family.

Rail bridges outside Ela are groups of this family under Bridge / Rail bridges: `World/Large Scenery/Bryson/Bridge 1`, `World/Large Scenery/Almond Bridge`, and each plate girder bridge below `World/Large Scenery/Plate Girder Bridge`. The track they carry is native map data and keeps its own trestle or steel flags; a modification moves only the bridge. The station groups `Cochran flagstopstation`, `Alarka Depot`, and `Alarka Jct Station` are switched by their progression controllers; `"active": false` holds through that switch, as [objects the game switches](#objects-the-game-switches) describes. Bryson Bridge 1 and the Almond Bridge are rail bridge constructions. Each carries a construction controller (`Game.Mow.TrackConstructionScenery`). It switches their parts on and off by the construction state of the track segments it names by ID, its construction segments. `"active": false` holds a group or part off even when the controller would switch it on; without it, or with `"active": true`, the controller switches the object. A rail bridge's `placement` must use `"mode": "fixed"`: a track-following placement of a rail bridge or of one of its parts fails before changing anything. The patcher reads construction segment IDs without resolving them, so a segment that no longer exists in the track graph causes no patcher failure. In Railroader 0.6.3, after a construction segment of Bryson Bridge 1 was removed, the parts stayed visible and the game reported an error on world load. See [Construction segments](#construction-segments).

Road bridges are groups of this family under Bridge / Road bridges: every group whose name contains `Road Bridge`, below `World/Large Scenery` and below the `World/Roads` objects that only hold other groups, including `World/Roads Bryson/US19 Gov Island/Concrete Road Bridge - 60m`, which shares its parent object with a grade-crossing setup. The road they carry is native map data; a modification moves only the bridge. A road bridge's `placement` must use `"mode": "fixed"`: a track-following placement of a road bridge or of one of its parts fails before changing anything, with `Road bridges use fixed placement. They don't support track-following placement.` Each road bridge holds a renderer culler and a scene ground mask on the bridge itself; see [scene ground masks](#scene-ground-masks).

Each crossbuck of an unprotected crossing is a group of this family under Grade crossing, wherever its crossing stands below `World`. The crossbuck itself is the group, such as `World/Roads Andrews/Crossing Unprotected - Robbinsville/Sign-Crossing0 (1)`. Its model and LOD levels aren't parts. A crossbuck accepts a fixed `placement` and `active`. The crossing pad and whistle signs beside it keep their own families and do not move with it.

Where unprotected crossings share one path, as the three crossings below `World/Roads Bryson/Old River Rd` do, their crossbucks also share a hierarchy path, such as `World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (1)`. Identify a crossbuck by that path and the target member `padMarkerId`, the marker ID of its crossing's pad (`Mbkd`, `M0sh`, or `Mgdu` there). Without `padMarkerId`, the shared path names every crossbuck at it, and the modification fails as ambiguous. A `padMarkerId` that matches no pad at that path identifies no crossbuck and also fails.

If the path uniquely identifies the crossbuck, the patcher refuses `padMarkerId` with `This crossbuck's path identifies it uniquely, so its target doesn't accept padMarkerId. Remove padMarkerId from this target.` Every other target refuses it with `The target doesn't accept padMarkerId because it isn't a crossbuck at an unprotected crossing with a shared path. Remove padMarkerId from this target.` A refusal changes nothing, and the other modifications still apply.

```json
"target": {
  "scene": "<scene from the inspected identity>",
  "path": "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (1)",
  "component": "UnityEngine.Transform",
  "structure": "scene-object-v1",
  "padMarkerId": "Mbkd"
}
```

A path that names no listed group, or more than one, fails the modification before changing anything. A modification may set `placement`, `active`, or both, and for Bryson Bridge 1 and the Almond Bridge also `relationships.constructionSegments`; see [construction segments](#construction-segments).

The patcher sets the group's physical placement and activity. Everything below it moves with it, and each part keeps its own activity. Placement uses map coordinates, converted through the current origin as for other objects below `World`. After a move, the patcher refreshes the culling sphere of every enabled renderer culler and game-object culler in the group, moves every simple culler's sphere, and synchronizes the physics scene. A disabled group's renderer cullers release their spheres. A simple culler on the group stops switching it while the group is disabled and resumes when it is enabled. Its hierarchy must hold no missing script and no rigidbody.

After a change, the patcher verifies the group's placement and activity. It also checks each part's placement relative to the group, or in map coordinates if the part has its own fixed placement, and each part's own activity unless a culler switches it. For a group or part that a construction controller switches, the patcher verifies its `active` value, not the activity the controller chose. Every enabled culler's sphere must stand at its culler. For a rail or road bridge, every collider must keep its shape and enabled state, and the physics bounds of a box, sphere, or capsule collider must follow its transform. For a rail bridge, the generated trestles within 250 m of the bridge must also stay unchanged. A rail bridge that holds a `Track.*` or `AutoTrestle.*` component is refused. A difference fails the modification and restores the captured group; an unverified restoration blocks further operations on the group for the load. See [move or disable a group and its parts](moving-scene-objects.md) for editing instructions and limits.

<a id="child-object"></a>

### Part

The patcher recognizes parts of groups in the Group family. The target `scene` and `path` name the part, its `component` is `UnityEngine.Transform`, and `structure`, when present, must be `child-object-v1`. The native map editor's Save writes the structure.

A part is a scene object at any depth below a listed group. It isn't a renderer or component. The scene must serialize it. It must have a renderer, collider, light, storage pile, or log pond at or below it, unless it holds a scene ground mask. See [scene ground masks](#scene-ground-masks). The following aren't parts:

- Objects below a group listed as inspection only.
- Siblings with the same name under the same parent, and objects below them.
- An object with a `KeyValueBoolAnimator`, and everything below it, because the game animates it.
- An object that only carries a simple culler. It sits below a group, has a simple culler (`Helpers.SimpleCuller`) and no renderer, collider, light, storage pile, or log pond of its own, and holds other objects. The objects it holds are parts, and their paths include it. A modification targeting it fails before any change and gives that reason.
- A LOD group level, which belongs to the object holding the LOD group.
- An object listed by another family.

A path that names no part or more than one fails before changing anything. A modification may set `placement`, `active`, or both. The patcher refuses `active` before changing anything for a part that a game-object culler switches by camera distance. The culler would override a stored setting, so the patcher cannot maintain it and never changes that part's activity. For a part that a rail bridge's construction controller switches, `"active": false` holds the part off against the controller.

`placement` is fixed in map coordinates, rather than relative to the group. Everything below the part moves with it. The group and other parts keep their placement and activity. A part with a fixed placement keeps it when its group or a part above it moves later, regardless of scene patch file, key order, or load order. The group's placement applies first, followed by each fixed part placement.

A part without a placement modification keeps its place relative to the object directly above it, whether the group or another part. Removing a part's modification in **Scene Patcher** returns it to its unmodified placement relative to that object at the object's current position.

After a change, the patcher verifies the part's placement and activity. It also checks that the group and other parts kept their placement and activity, and that objects below the part kept their placement relative to it. It refreshes every enabled culling sphere in the group, and every culler keeps the radius the scene gives it. A part moved outside the sphere of a culler above it keeps its own culling sphere, centered on the part's renderer, collider, and light bounds and enclosing them. The patcher registers that sphere with the game's culling manager and removes it when the part is back inside the culler's sphere, is disabled, or loses its modification. A part that was never moved gets no sphere of its own. The scene patch file holds no field for own culling spheres. A difference fails the modification and restores the captured part; an unverified restoration blocks further operations on the part for the load.

#### Storage piles and industry buildings

The storage displays `Andrews Snowbird Paper`, `Sylva Paperboard`, and `Connelly` below `World/Large Scenery` are groups, and each storage pile in them is a part, such as `World/Large Scenery/Whittier/Connelly/Log Pile L1a` or `World/Large Scenery/Andrews/Andrews Snowbird Paper/Pulpwood Piles/Pile (3)`. Target each group with `scene-object-v1` and each part with `child-object-v1`. A pile shows the storage of its industry through a `Scenery.BulkIndustryStorageSynchronizer`, which names the industry component and the load by ID. Neither `placement` nor `active` changes that storage.

After a change, every enabled pile in the group that has moved updates its culling sphere to its new position. If the pile is showing logs, it lays them out again there. A pile culled by the camera lays them out when it becomes visible again. If a sphere isn't at the pile's new position with the pile's radius, the modification fails and restores the group or part. The game builds a pile's collider child while the camera is near. The scene doesn't save that child, and it isn't a part, but it moves with the pile. If the game members needed to refresh piles are unavailable, the patcher refuses edits to any group that contains a pile and to its parts before making any change.

Industry positions, native track spans, and their delivery and loading points are native map data in `Map.json`. No modification targets them, and moving a decorative industry building or a storage display leaves them in place, so loading and unloading on the native track spans are unchanged.

#### Lake surfaces

A lake surface is a `LakePolygon` with its water mesh, saved in the scene. The groups `World/Large Scenery/Whittier/Whittier Log Pond`, `World/Rivers/Fontana Lake (1)`, and `World/Rivers/Fontana Lake (2)` hold one each. Target each group with `scene-object-v1` and each part with `child-object-v1`. `Whittier Log Pond` has two parts. `Whittier Saw Mill Log Pond` is the water mesh, and `LogPond` is a `Scenery.LogPond` that lays out the logs of the sawmill's storage. A Fontana lake has no parts and has a renderer culler on the lake itself.

The patcher sets the placement of the object that carries the water mesh, or of the group above it. The lake masks and rivers in `Map.json` are native map data. They shape and color the terrain. No modification targets them, and moving a lake surface leaves them in place.

A modification that sets `placement` on a group or part with a lake surface whose mesh renderer is part of a static batch fails before any change, because the game would keep drawing the batched water at its old place. `active` alone still applies.

The log pond shows its storage through a `Scenery.BulkIndustryStorageSynchronizer`, as a storage pile does, and neither `placement` nor `active` changes that storage. After a change, the patcher moves the log pond's culling sphere and has it lay out its logs again where it stands, under the same checks as a pile; a log pond builds no collider.

#### Scene ground masks

A scene ground mask is a `Map.Runtime.MaskComponents` component that the scene saves. It registers a shape in the game's terrain system where its object stands. Each station and depot group holds a `RectangleMapMask` on a part named `Mask`. The inventory names that part on the group and any part above it. Target the groups with `scene-object-v1` and their parts with `child-object-v1`. The object that holds a mask is listed as a part even without a renderer, collider, or light.

When a placement changes a group or part that holds a mask, or stands above one, the patcher checks the enabled masks affected by the edit. It rebuilds each mask that is no longer at its holder. The game's `Rebuild` registers the mask once, with its center and rotation at the holder. Rebuilding the mask makes the game regenerate terrain and vegetation at the old and new positions. The patcher tracks that terrain output for the edited object at the mask's center. Save doesn't write the output. A part with a fixed placement keeps its mask where it is when its group moves.

The patcher expects `"active": false` to release a mask and `"active": true` to register it again. If the mask doesn't agree with the activity, the edit fails. This hasn't yet been observed in Railroader.

A mask that also flattens the ground moves in the same way. The patcher rebuilds the mask at the object that holds it. The game then rebuilds each affected terrain tile from its base heights and the masks registered on it. At the old position, it restores the ground height without the mask. At the new position, the mask flattens the ground. In Railroader 0.6.3, all station and depot masks flatten the ground.

A bridge (`bridge-colliders-v1`), a milepost (`milepost-v1`), a crossing pad (`crossing-pad-v1`), a station map icon (`station-map-icon-v1`), and a spawn point (`spawn-point-v1`) move the enabled scene ground masks on the object and its children in the same way, including masks that flatten the ground. The patcher tracks the terrain output for the edited object. No such object holds a mask on the Railroader 0.6.3 base map.

A road bridge can hold its mask on the bridge itself: a `RectangleMapMask` as long as the bridge, which the Railroader 0.6.3 scene sets up to cut trees. A placement of the group rebuilds the mask at the bridge and tracks the terrain output for the group; a placement of a part leaves the mask where it is. The mask of `World/Roads Dillsboro/Old Home Town Road Bridge` also flattens the ground and moves with the group in the same way. The mask component of `World/Roads Whittier/Whittier Road Bridge` is disabled in the scene: it holds no registration, a placement or an `active` change of that group rebuilds nothing and starts no terrain output, and the inventory names no ground mask for it. In Railroader 0.6.3, a move of Whittier Road Bridge showed no mask dependency and started no terrain output.

The Generic track marker on the root of Hemingway flagstopstation keeps its own `track-marker-v1` target. The station isn't in the Group family. A placement of the marker moves the ground mask below it, which also flattens the ground. The patcher handles this move and any `active` change in the same way as for a group. Every other `track-marker-v1` target does the same with the masks on the object and its children. This includes a mask on a location sign, such as a `Loc:` sign, that moves with its parent. A `location-sign-v1` target moves its own mask with the location sign.

In Railroader 0.6.3, the US19 Gov Island road bridge's tree-cutting mask moved with the bridge. A test build that moved Wilmot Station with its mask showed flat ground at the new position and vegetation back at the old position, judged by eye. Ground heights after Undo and removal have not been compared numerically in play. The automated tests use a controlled mask and terrain system to check that masks are rebuilt, including those that flatten the ground. They also check the terrain output, masks under the other families above, the Hemingway track marker, restoration, Undo, Redo, Save, and reload. A mask whose shape crosses a terrain tile border is tracked on the tile of its center only, and **Retry** asks again only for that tile; location signs behave the same way. A road bridge mask is as long as its bridge, up to 200 m for `Almond Road Bridge` and 60 m for the bridge at US19 Gov Island, so the tracked output can complete while a neighboring tile is still being rebuilt. Without the game's ground mask members, the patcher refuses groups that hold a mask and all their parts. The Hemingway marker is not refused and would move without its mask.

#### Buildings with pickable toggles

The groups `World/Large Scenery/Bryson/Bryson Section House`, `World/Large Scenery/Bryson/Bryson Tower Chance Clayton`, `World/Large Scenery/Bryson/Bryson Turntable`, and `World/Large Scenery/Dillsboro Turntable/Roundhouse 3` are listed under Railroad service building. They hold pickable toggles (`RollingStock.Controls.KeyValuePickableToggle`). Target the groups with `scene-object-v1` and their parts with `child-object-v1`.

- A lantern's toggle stands on its `TrainmanLantern` part, with the `Effects.LanternController` that switches the lantern: `.../Bryson Section House/TrainmanLantern` and `.../Bryson Tower Chance Clayton/Culling Container/TrainmanLantern`. Every frame, the game turns the lantern's flame toward the camera and scales it. The flame is the object named by the controller's `filamentRenderer`. It and every object below it aren't parts, but move with the lantern.
- A stall's toggle stands on `Roundhouse Modular A Stall` below each stall, with the `KeyValueBoolAnimator` that opens its doors. That object and everything below it are not parts; the stall above it is, such as `.../Bryson Turntable/Roundhouse/Stall (2)` or `.../Roundhouse 3/Stall (1)`. A modification that names the animated object or a door fails before any change.
- The tower's name text is the part `.../Bryson Tower Chance Clayton/Culling Container/Sign-Station/Text`, a `TMPro.TextMeshPro` whose text the scene saves. `Culling Container` only carries a simple culler, so it is not a part.

In the game code, a toggle is picked through a collider at or below its object and flips one Boolean key of the nearest key-value object (`KeyValue.Runtime.KeyValueObject`) above it; it reads no position. That key-value object carries a `RollingStock.Controls.GlobalKeyValueObject` with a `globalObjectId`; the game save keeps the key values by that ID, not by position. It stands on the `Bryson Section House` and `Roundhouse 3` groups themselves, on the `Roundhouse` part of `Bryson Turntable`, and on the tower's `Culling Container`. In Railroader 0.6.3, the moved Bryson Section House lantern and Bryson roundhouse doors worked at their new positions. Their states survived Save & Quit and reload. `placement` and `active` never write a key, an ID, or a stored value. With `active` set to `false` on a toggle's object or above it, the toggle cannot be clicked. With `active` set to `false` on the key-value object's object or above it, the game unregisters its ID, so a game save made while it is disabled might not keep the stored values. The patcher does not refuse such a modification. The turntable and the tracks into the stalls are native map data in `Map.json`; no modification targets them.

### Spawn point

The patcher recognizes every scene object with a `Character.SpawnPoint` component, such as the spawn points under `World/POIs`. Set the target `component` to `Character.SpawnPoint` and `structure` to `spawn-point-v1`; the native map editor's Save writes the structure. Use the `scene` value from the inspected identity. A path that names no loaded spawn point, or more than one, fails the modification before changing anything. A modification may set `placement`, `active`, or both.

The patcher moves the spawn point's transform and sets its activity. A spawn point has no renderer, collider, or culler. In Railroader 0.6.3, a new East Whittier company game started at its moved spawn point when the map had no native spawns. The `/tp` command used the moved Alarka point. Rescue and the precedence between native spawns and a company start setup remain unverified.

The patcher applies eligible spawn point modifications before the new game reads the start point. Modifications with `dependsOn` entries or a `trackFollowing` placement wait for the normal world load. After a change, the patcher verifies placement and activity. If verification fails, it restores the captured state.

Native spawns in the `spawns` section of `Map.json` are native map data. The native map editor edits them in its **Spawns** category, and the patcher does not list or change them. See [move player spawn points](moving-spawn-points.md) for editing instructions.

### Existing crossing mast and bell

The patcher recognizes the masts and bell of an existing crossing. A mast's target `scene` and `path` identify its light object, such as `.../Grade Crossing - Bryson Everett/Signal 1/Signal 1`. Its `component` is `Track.KeyValueLampMaterialController`. If `structure` is present, it must be `existing-crossing-mast-v1`. The gate on a child of the light object is part of the mast and cannot be targeted separately.

A bell's target identifies the bell player that the crossing controller creates at `.../Crossing Bell`. Its `component` is `Audio.IntegerLoopingPlayer`. If `structure` is present, it must be `existing-crossing-bell-v1`. The native map editor's Save writes the structure for both masts and bells.

A mast matches `existing-crossing-mast-v1` when it meets these requirements:

- Its light object is below a crossing controller, and that controller's key-value object is the nearest key-value object above it.
- Each lamp and gate on or below the light object observes the `lights` or `gates` key published by the controller.
- Every lamp target is inside the mast's hierarchy.
- No gate is elsewhere under the light object's parent.
- The mast contains no second light object, crossing controller, key-value object, detection span, game-object culler, bell player, rigidbody, or missing script.
- No track-relative component switches the mast with a track group.
- No other loaded object shares its hierarchy path.
- The patcher did not add the equipment.

A bell matches `existing-crossing-bell-v1` when it is the bell player created by its crossing controller and the controller is enabled. If a target matches no object, matches more than one object, or does not match its supported source pattern, the modification fails before changing anything and reports the reason. Other modifications still apply. See [refusal reasons](moving-crossing-equipment.md#refusal-reasons).

A modification may set `placement`, `active`, or both. Each is a separate operation. The modification accepts no `trackMarker`, `coverage`, `equipment`, or `starter` relationship. Changing placement or activity preserves the equipment's logical binding to its crossing controller, which continues to drive the same mast or bell.

`placement` uses fixed placement in map coordinates. The mast's light and gate move and rotate together. The crossing controller, other masts, and bell keep their physical placement. Without a placement modification, the mast or bell keeps the scene's placement relative to its parent. Removing the modification in **Scene Patcher** returns it there. A modification whose `placement.mode` is `trackFollowing` fails before changing anything and reports that track-following placement is unsupported. A bell's sound has no direction, so its rotation has no effect. The editor shows the rotation but does not let you set it.

`active` sets the activity of the mast's light object or the bell itself. The patcher applies `"active": false` on every load, and the equipment stays disabled as the game's culler hides and shows the crossing. Without that value, the equipment follows the game as before. The bell player discards its sound when disabled. When the bell is enabled again, the patcher configures the player with the crossing's loaded bell sound.

If a game-object culler lists the equipment itself, the patcher refuses `active` before changing anything because the culler would override the saved value. At the Bryson Everett crossing in Railroader 0.6.3, the crossing's culler lists each mast's parent, so `active` is supported for those masts.

A crossing's game-object culler keeps its radius. A mast moved outside that culler's sphere gets a sphere of its own, as a moved [part](#child-object) does. The patcher removes the listed object above the mast from the crossing's culler list and gives that object a sphere that stands in for the culler's, with the same center and radius. The mast uses its own sphere, sized around the mast and the gate arm's position at that moment. The patcher returns both objects to the game's culling when the mast returns inside the crossing's sphere, the modification is removed or restored, or the patcher's state for the world ends. No culler switches a bell, and the patcher gives it no sphere.

After a change, the patcher checks the equipment's physical placement and activity. It also verifies that the crossing controller and every other object below it kept their physical placement and activity, and that each culling sphere is where the plan requires it. A difference fails the modification. The patcher restores the captured placement, activity, and culling membership. If it cannot verify the restoration, it blocks further operations on that mast or bell for the load. See [move or disable an existing crossing mast or bell](moving-crossing-equipment.md) for editing instructions and limits.

### Track marker

The patcher recognizes an object holding one Generic `Track.TrackMarker`. The target `component` is `Track.TrackMarker`; set `structure` to `track-marker-v1`. The object must meet these requirements:

- It is not a milepost or a crossing pad. A Generic marker inside a crossing setup, such as a whistle sign, qualifies.
- No other component listens to the marker's location changes.
- A `Track.TrackRelativePosition` whose activity follows a track group activates this object itself, and no other component has replaced its activation callback.
- No loaded component other than that relative position holds a direct reference to the marker. If a scan cannot read a field able to hold a marker, it refuses every marker.
- No new-game car placement uses the marker.

A modification may set `markerId`, `relationships.trackMarker`, `placement`, `active`, or any combination. See [replacement ID](#replacement-id). For a marker that a progression controller or a map feature switches, such as `Sign Begin CTC Sylva`, `active: false` holds through that switch; see [objects the game switches](#objects-the-game-switches).

A `trackMarker` setting writes the marker's serialized location and its named-location registration in the track graph. The patcher removes the registration under the marker ID, writes the location, and registers the marker at the new location when it is enabled and active. A detach leaves no named location. The patcher registers map labels below the marker with the map again, so their dimming follows the new location's track group. Placement moves the object root and refreshes its renderer cullers. It never changes the binding.

Before changing anything, the patcher requires exactly one registration of this marker at its current location if the marker is enabled, active, and has a resolvable location. Otherwise, it requires none. After a change, it verifies the location, placement, activity, registration rule, and map labels' track group. A difference fails the modification and restores the captured location, registration, placement, and activity. If the patcher cannot verify the restoration, it blocks further operations on the marker for the load. See [repair, replace, or detach a track-marker binding](repairing-track-bindings.md) for editing instructions and limits.

#### Markers with group-driven activity

When a marker object's `Track.TrackRelativePosition` makes its activity follow a track group, it subscribes to that group's event in the track graph. The group is the one containing the marker's bound segment. The game activates or deactivates the object with the group. For such an object:

- A `trackMarker` setting also moves that subscription: after the location change the patcher runs the relative position's own unsubscribe and observe steps, so exactly one subscription observes the new segment's group. A detach, or a location that does not resolve, leaves no subscription.
- `active: false` is an explicit disable. The patcher replaces the relative position's activation callback with a hold that keeps the object inactive, before anything else changes, so a later group activation cannot activate it, not even briefly, including during a repeated application.
- An omitted `active` leaves the object's activity as it is. Without an explicit disable the game's group activates and deactivates the object. An explicit disable from another modification of the same object stays in place, whether that modification is in the same scene patch file or in another mod's scene patch file.
- `active: true` is refused before changing anything, because the object's activity belongs to its track group. Omit `active` to keep group-driven activity.
- Removing the disable in the native map editor, by clearing `active` or removing the modification, releases the hold and observes the group again, so the object's activity follows the group's current state.

After a change the patcher also verifies the subscription count, the observed group, and the activity the group or the hold requires. A difference fails the modification and restores the captured location, registration, subscription, hold, placement, and activity; an unverified restoration blocks further operations on the marker for the load.

#### Markers with a duplicate marker ID

Removing one marker's named location removes every track location registered under its marker ID, on every segment. This removes the named location of every scene marker holding that ID. If the target holds a duplicate marker ID, any modification that would change its activity, change its `trackMarker` binding, or set a `trackFollowing` placement fails before changing anything.

The holders are the loaded scene markers with the target's marker ID and every scene marker owning a registration under it, whether loaded or not. This rule applies to Generic markers and crossing pads. The failure is an invalid supplied definition. Its diagnosis names the duplicate marker ID and every holder, including each holder's activity.

The file's other modifications still apply, including a fixed `placement` of the same marker. The exception is a marker with a map label below it that caches a different track group from the one at its location. Correcting that cache would require registering the marker again. A value equal to the marker's current value changes nothing and still applies, such as `"active": false` on an inactive marker or the location the marker already has.

A modification that also sets `markerId` on the target to a different ID that is not in use is not refused for this reason. A holder that keeps the shared ID needs `dependsOn` for its own activity, binding, or track-following settings, as described in [replacement ID](#replacement-id). The replacement ID applies first, so the target's activity, binding, and track-following settings act under its own ID, and the other holders keep their registrations. See [replacement ID](#replacement-id). A `markerId` equal to the marker's current ID does not lift the refusal. A crossing pad takes no replacement ID, so its settings stay refused while another holder keeps its ID. A registration that another holder owns does not count as a registration of the target, so it does not fail the registration rule before changing anything.

### Crossing pad

The patcher recognizes an object holding one `Track.TrackMarker` of type `Crossing` or `Generic` and one `Track.TrackCurveMeshBuilder` with a mesh container and a mesh profile. The target `component` is `Track.TrackMarker`; set `structure` to `crossing-pad-v1`. A pad target named with `track-marker-v1` does not resolve, and a Generic-marker target never resolves to a pad. The track-marker consumer rules apply, except that the pad builder may hold the marker, a pad inside a crossing qualifies, and a pad whose activity follows a track group is refused. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination.

A `trackMarker` reference on a pad moves the marker's stored track location and its named location as for a Generic marker, then runs the game's pad generation. Generation rebuilds the pad mesh along the track at the new location and moves the culling sphere to the new bounds; the game's culling recomputes the mesh visibility on its next update. `{ "detach": true }` fails the modification before changing anything, because a pad without a location has no geometry; disable the pad with `"active": false` to hide it. A placement change also regenerates the pad, so the mesh stays on the track while the object moves.

For a pad with `placement.mode` `trackFollowing`, the patcher also records the track curves the pad was generated from, the segment curves within half the pad width on either side of the marker location, and regenerates the pad when they change, even if its placement does not. The editor checks for changed curves while reconciling track-following placement, including after native Undo and Redo of track edits. It regenerates the pad once the changed curves have held still between two checks. A pad without track-following placement is not regenerated after a track edit by itself; every application of a pad modification, and its removal, regenerates the pad when the recorded source curves differ from the current track.

After a change the patcher verifies the track-marker rules. It also checks that the current mesh was generated from the mod's location and the pad's current position, and that the culling sphere encloses that mesh. A pad generated by the game before the patcher observed it is generated once more on the first application, to the same mesh; later applications leave current geometry alone. A difference fails the modification and restores the captured location, registration, placement, and activity, generates the pad again and requires the regenerated mesh to match the captured one; an unverified restoration blocks further operations on the pad for the load.

One native journey on Railroader 0.6.2 with patcher commit `a282015` observed the pad Mg74 shift along the track after a rebinding, refusal of a missing segment and an out-of-range distance, native Undo and Redo, removal, and Save with a native map editor reload that reproduced one modification and one registration. A second journey with commit `ab2816f` measured that the rebinding changed the pad's vertex hash and moved its culling sphere 0.993 meters along the track while the sphere stayed aligned with the new bounds, and that native Undo and Remove modification restored the original vertex hash, bounds, and culling sphere exactly. A pad that holds a duplicate marker ID follows the rule under Markers with a duplicate marker ID. See [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md) for editing instructions and limits.

### Milepost

The milepost adapter recognizes an object with one `Helpers.MilePost`, the Generic `Track.TrackMarker` it requires, and its `Helpers.TextSynchronizer`. Set the target `component` to `Helpers.MilePost` and `structure` to `milepost-v1`. A milepost target named with `Track.TrackMarker` or `track-marker-v1` does not resolve to the milepost. The patcher refuses a Generic-marker target on a milepost before changing anything. The track-marker adapter's consumer rules apply, except that the milepost itself may hold its marker and listen to its location changes. The object must have no missing script and no `Track.TrackCurveMeshBuilder`. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination. Each is a separate setting.

A `trackMarker` setting writes the milepost marker's stored track location and its named location as for a Generic marker, including the track-group subscription when the milepost's activity follows its track group. A detach leaves no named location. Placement moves the milepost root and refreshes its renderer cullers without changing the binding. `active: false` is an explicit disable. For a milepost whose activity follows its track group, `active: true` is refused before mutation, as under Markers with group-driven activity.

These settings never change the milepost's model, mile value (`mileage`), prefix, origin flag, or displayed text. They never recalculate a mile value. The first time something asks a milepost for its origin, the game caches the origin milepost object for that prefix. The cache holds the object, not its location. A placement or binding edit leaves the entry absent if it was absent, or holding the same object if it was present.

After a change, the adapter verifies the track-marker rules and checks that the prefix, origin flag, mile value, displayed text, model, and origin cache entry match their values before the change. It checks rendered texts only if the milepost was active before the change, because text that has never been awake fills in when the milepost first becomes active. A difference fails the modification and restores the captured location, registration, subscription, placement, and activity. If the restored milepost state still differs from the captured state, the patcher reports incomplete cleanup and blocks further operations on that milepost for the load.

The capability is incompatible with a game build whose milepost does more than call an empty `UpdateMileage` when its marker's location changes. The patcher does not repair a milepost binding before the game's marker check at world load. A broken binding therefore still raises the game's warning once in that load; the patcher repairs it during normal application. A milepost with a duplicate marker ID follows the rule under Markers with a duplicate marker ID. See [reposition or rebind a milepost](repositioning-mileposts.md) for the editing route and limits.

### Crossing

The patcher recognizes an object holding one `Track.Crossings.GradeCrossingController` whose circuit list is non-empty and whose circuits each hold an approach A, island, and approach B span with an ID. The target `component` is `Track.Crossings.GradeCrossingController`; set `structure` to `crossing-coverage-v1`. A modification may set only `relationships.crossingCoverage` and `relationships.crossingEquipment`; `placement`, `active`, and `trackMarker` are refused before changing anything.

The patcher records the crossing's circuits and the map's own detection spans when it first observes them in the load. At that point, every detection span from the map must be registered in the track graph under its ID.

For a selected circuit, the patcher creates three disabled owned spans under a root that isn't saved. It sets their IDs and endpoints, then enables them so the game's track span lifecycle registers them. The circuit then refers to those spans. Repeated application reuses matching owned spans. Detaching a circuit removes it from the controller's circuit list but keeps its references to the map's own detection spans. From its next pass, the controller evaluates the remaining circuits in their native order.

A detection addition gets a new circuit set with three owned spans, built the same way. Added sets go at the end of the controller's list. The patcher remembers each set by its identity. If an adapter is later created over a world that still holds that set, it reconciles the existing set without building another. The patcher disables and destroys owned spans that no circuit uses. Disabling them removes their registrations.

Before changing anything, the patcher checks that the controller evaluates only its native circuits in native order, followed by the added circuits. Each native circuit must hold the map's own detection spans for that circuit or owned spans, and every detection span from the map must stay registered.

After a change, it checks those rules again, along with each circuit's coverage and each addition's tracks. No other added circuit may remain, and exactly the owned spans in use must be registered and cover track. A difference fails the modification and restores the captured coverage, removing any partly built addition and returning the captured additions. If owned spans remain registered after restoration, cleanup is unverified and further operations on the crossing are blocked for the load.

The native map editor's Save does not write the controller's references, added circuits, or owned spans. Each load rebuilds them from the scene patch file. See [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md) and [add detection for extra tracks at an existing crossing](adding-crossing-detection.md) for editing instructions and limits.

For each equipment addition the patcher creates a disabled wrapper with `HideFlags.DontSave` beneath the controller. It records the scope, ID, pattern, and source on the wrapper and instantiates the source beneath it at the placement. It checks that the copy's lamps and gates are its own components, that its gates keep their clip, and that the controller's key-value object is the copy's nearest one. The patcher then adds the copy to the controller's `Helpers.Culling.GameObjectCuller` list with the culler's current state and enables the wrapper. The copy's lamps and gates initialize through the game's restore notifier and observe the controller's keys. Removal takes the copy out of the culler list, then disables and destroys the wrapper.

When the patcher first observes a controller in a load, it adopts wrappers left beneath it by an earlier application. Repeated world-load callbacks and retained worlds therefore reconcile without duplication. A crossing that no longer stands under its identity releases its additions with its owned spans. A construction failure removes the partial copy and restores the captured equipment; a copy that cannot be released makes cleanup unverified. See [add lights and gates to an existing crossing](adding-crossing-equipment.md) for editing instructions and limits.

For a bell, the patcher creates the same wrapper at the specified placement and records the bell definition as its source. An object below the wrapper holds an `Audio.IntegerLoopingPlayer` with the crossing bell's mixer group, priority, and audio distance. Through the controller's key-value object, the player observes the bell key's current value and every change.

The patcher loads the definition's audio through the content library and keeps the audio reference on the bell. Once loading completes, it configures the player with the clip, indexes, and volume from the definition. Removing the bell stops the player, disposes the observer, cancels any pending load, releases the audio reference, and destroys the wrapper. If a load completes after removal, it releases its reference immediately. Bells do not join a culler. See [add a bell to an existing crossing](adding-crossing-bells.md) for editing instructions and limits.

### Starter placement marker

The patcher recognizes an object with one Generic `Track.TrackMarker` that a car placement of an active new-game setup (`Game.Progression.SetupDescriptor`) uses. The target `component` is `Track.TrackMarker`; set `structure` to `starter-placement-v1`. The marker must have an ID, and the object must not be a milepost or crossing pad. No other component may listen to the marker's location changes, and its activity must not follow a track group. No loaded component other than a colocated `Track.TrackRelativePosition` may hold a direct reference to it. Such a marker never resolves as `track-marker-v1`. A modification may set only `starterPlacement`.

A `starterPlacement` track location writes the marker's serialized location and track-graph registration as for a Generic marker. The patcher removes the registration under the marker ID, writes the location, and registers the marker at the new location if it is enabled and active. A disabled marker has no registration, but the game still reads its location when starting a new company game. The object's placement and activity stay as they are.

A `wreck` writes the `wreck` field of the one `SetupDescriptor.CarPlacement` that reads the marker. Omitting `track` leaves the marker's location and registration untouched. Before changing anything, the patcher checks the track-marker registration rule and refuses a marker whose ID another scene marker holds, as it does for track markers. After a change, it verifies the location and that rule. A difference fails the modification and restores the captured wreck setting, location, and registration. An unverified restoration blocks further operations on the marker for the load.

The game reads the location and the wreck setting in `Game.State.CompanyModeSetup.Setup`, which runs when a new company game becomes active, after the world-load callback in which the patcher applies its scene patch files. The whole-cut check walks the track with the graph's own `LocationByMoving`, reads the segments' availability, and takes each cut's length from `StateManager.DescriptorsForIdentifiers` and `ConsistLength.OfDescriptors`, as the game's `TrainController.HandlePlaceTrain` does before `CanPlaceAt`. See [place a starter placement marker on the track](placing-starter-markers.md) for editing instructions and limits.

## Writing by the native map editor

The native map editor's Save writes indented UTF-8 JSON with line-feed line endings and no byte-order mark. It uses the canonical order below and writes one modification per edited object. A new modification's key depends on the object's first edit:

- `bind-<object name>` for a track-marker relationship.
- `cover-<object name>` for crossing coverage.
- `equip-<object name>` for crossing equipment.
- `starter-<object name>` for a starter placement.
- `follow-<object name>` for track following.
- `marker-id-<object name>` for a replacement ID.
- `place-<object name>` otherwise.

Save adds a numeric suffix to make the key unique. If the object name already starts with the prefix, Save writes that prefix once. The marker `starter-ewh-int2` therefore gets the key `starter-ewh-int2`. Save doesn't keep the source's whitespace or comments.

Reading a scene patch file never reorders it. Save writes the file only when it has an outstanding change. That write puts it in canonical order.

The scene patch file is Saved exactly when its current content and the text last read or written have equal content. Texts that can't be parsed have equal content only when their text is identical. For parsed texts, object members must match at every depth, regardless of order. If a modification's `dependsOn` is an array of strings, the lists must match after sorting in ordinal order, with duplicates counted. Every other array must match in order. Strings, booleans, and null must match exactly. Numbers compare by value.

Numbers are equal when converting each value to the nearest IEEE 754 double gives the same result. For example, `1`, `1.0`, and `10E-1` are equal, as are two literals that differ only beyond double precision. In parsed texts, whitespace and comments don't affect equality. An edit that leaves equal content leaves the scene patch file Saved. The next Save writes nothing and leaves the file's bytes unchanged. If no scene patch file existed at load, it is Saved while it has no modification.

Save refuses to overwrite a file that changed outside the editing session. Its current content must equal the text last read or written, using the comparison above. Reordering or changing spacing outside the editing session does not count as a change and does not block Save. You must choose a version if a value changed, the file is unparseable, the file was deleted since it was read, or a file was created after a load that found none. A file that needs repair requires a verified backup before the editor version can replace it. If the file was deleted, **Keep editor version** needs no backup and the next Save writes the editor version.

The editor preserves unknown fields at the scene patch file root and unknown modifications without executing them. Every number the native map editor did not replace keeps its source text. For example, it writes back `0.10` or a number beyond double precision unchanged, even inside a modification changed in the editor. When the engine tries to execute a modification, it rejects unknown fields inside that modification, its target, placement, or relationships. The native map editor preserves such a modification without editing or removing it.

The native map editor protects and does not write a scene patch file in any of these cases:

- Its format version is missing, malformed, or unsupported.
- `modifications` is an array or any other value that is not an object.
- A modification key is empty or a modification is not an object.
- A property name repeats in any object.
- The file is for another map.
- Its numbers cannot be matched to their source text.

Format version 1 has no earlier released shape. The editor doesn't convert an array form of `modifications`. For a protected scene patch file, it shows the problem and offers to read the file again after you correct it outside the editor. You can also start fresh after making a backup. See [Recover a protected scene patch file](recover-scene-patch-files.md).

### Detecting an outside file change

The patcher compares the file on disk with the scene patch file it last read or wrote, using the equal-content rules above. Whitespace, object-member order, and the order of entries keyed by stable IDs do not affect equality, so reformatting alone does not trigger a version choice. When content differs, the editor offers **Use file version**, **Keep editor version**, and **Import**. Until you choose one, Save writes nothing and shows the reason.

Import compares the file on disk with the text last read or written. It also compares the session's scene patch file with its state at the start of the session. A Save during the session therefore creates no conflict by itself. Import checks the declared `formatVersion` before any other content. It keeps unknown members of the file and its modifications without executing them, and a later Save writes them unchanged.

The comparison runs when the game regains focus and when you click **Refresh** or **Save**. See [Edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md#choose-a-scene-patch-file-version).

### Construction segments

`constructionSegments` sets the construction segments of a rail bridge construction. Its construction controller (`Game.Mow.TrackConstructionScenery`) reads these track segments' construction state to show or hide the bridge's parts. This setting requires patcher 0.5.0 or later and supports two kinds of target:

- The group of a rail bridge construction that carries its controller: `World/Large Scenery/Bryson/Bridge 1` and `World/Large Scenery/Almond Bridge`, with `UnityEngine.Transform` as `component` and `scene-object-v1` as `structure`.
- A construction controller on an object of its own, `World/Large Scenery/Ela/Ela Bridge Scenery`, with `Game.Mow.TrackConstructionScenery` as `component` and `bridge-construction-scenery-v1` as `structure`. Its construction segments are its only setting: a `placement` or `active` that differs from the object's own fails before changing anything, because the controller's position has no effect in the game and the bridge's construction visibility runs on it.

| Setting | Meaning |
| --- | --- |
| Omitted | The construction keeps its native segments. |
| `{ "segments": [<segment ID>, ...] }` | Replaces the construction segments with exactly these segments. |
| `{ "detach": true }` | Fails before changing anything: a construction without segments has no meaning. |

`segments` lists at least one segment ID, each once, in any order. The native map editor's Save writes them in ordinal order. The modification fails before changing anything if a segment is missing from the loaded track or more than one loaded segment carries its ID. The message names that segment, and the patcher never substitutes another one. The patcher accepts the construction's native segments as they are, even when one no longer exists. The modification also fails on any other target, including a plate girder bridge or a part of a construction.

The patcher writes the segment IDs to the controller and clears its cached segments. If every segment exists and the controller is enabled, it updates construction visibility. Otherwise, the parts keep their visibility until the world reloads. The patcher verifies the controller's segment IDs after writing them. Applying the same segments again writes nothing. A failure after the write restores the controller's pre-edit segments. An unverified restoration blocks further operations on the object until **Recheck** verifies the state or the world reloads. Removing the modification returns the native segments.

At world load, the patcher defers the controller's update if a native segment is missing and the last acceptable setting in load order supplies a complete replacement set on the loaded track. A later setting that restores the native set takes precedence and leaves the native update in place. The game runs a deferred update one frame after the patcher applies the scene patch files. In Railroader 0.6.3, Bryson Bridge 1 loaded without the stale-segment error after repair and showed or hid parts as the new segment's construction state changed. See [repair the construction segments of a rail bridge](repairing-construction-segments.md) for the cases where the native error still occurs.

### Canonical order

When Save writes the scene patch file, it uses this order:

- Scene patch file root: `formatVersion`, `sourceMap`, `modifications`, then unknown members.
- `sourceMap`: `mapId`, then unknown members.
- The keys of `modifications`, `circuits`, `additions`, and `crossingEquipment` are in ordinal order.
- A modification: `target`, `markerId`, `relationships`, `starterPlacement`, `placement`, `dependsOn`, `active`, then unknown members.
- `target`: `scene`, `path`, `component`, `structure`, `padMarkerId`.
- `relationships`: `trackMarker`, `crossingCoverage`, `crossingEquipment`, `constructionSegments`.
- `constructionSegments`: `segments`, `detach`. The values of `segments` are in ordinal order when it is an array of strings.
- `crossingCoverage`: `circuits`, `additions`.
- `starterPlacement`: `setup`, `track`, `wreck`.
- An entry under `circuits` or `additions`: `approachA`, `island`, `approachB`, `detach`.
- An entry under `crossingEquipment`: `pattern`, `source`, `placement`.
- `source`: `scene`, `path`, `definition`.
- A span: `from`, `to`.
- A track location, including a `trackMarker` reference and a `starterPlacement` track: `segment`, `end`, `distance`. A track-following `track` that names an endpoint holds only `node`, and a detach object holds only `detach`.
- `placement`: `mode`, `position`, `track`, `offset`, `rotation`. A fixed placement uses `mode`, `position`, `rotation`; a track-following placement uses `mode`, `track`, `offset`, `rotation`.
- The values of a modification's `dependsOn` are in ordinal order, duplicates kept, when `dependsOn` is an array of strings; otherwise `dependsOn` keeps its source form, including the member order inside its items.
- Every other array keeps its source order.
- In every object, unknown members follow the known members, in ordinal name order. This holds at every depth, including inside unknown objects and inside the objects of unknown arrays.
- Every number that an edit does not replace keeps its source literal.

Ordinal order compares strings by their UTF-16 code units, one unit at a time. A string that is a prefix of a longer string sorts first. `B` sorts before `Z`, `Z` before `_`, and `_` before `a`. A character outside the Basic Multilingual Plane sorts by its surrogate code units, so it sorts before U+E000 to U+FFFF.

## Dependency minimum

The supplying mod's `Info.json` must list `crankyant/scene-patcher` under `dependencies.required`. Otherwise, the runtime skips that mod's scene patch file. The native map editor's Save adds the entry or raises its minimum version.

| Scene patch file content | Minimum patcher version |
| --- | --- |
| Fixed placement only | `0.1.0` |
| Any modification with `active` | `0.2.0` |
| A location sign target other than the legacy Sylva location sign | `0.5.0` |
| Any modification whose target `structure` is `bridge-colliders-v1` | `0.5.0` |
| Any modification with `relationships`, a `Track.TrackMarker` target component, or target `structure` `track-marker-v1` | `0.5.0` |
| Any modification with `markerId` | `0.5.0` |
| Any modification with `active` whose target `structure` is `track-marker-v1` | `0.5.0` |
| Any modification whose `placement.mode` is `trackFollowing` | `0.5.0` |
| Any modification whose target `structure` is `existing-crossing-mast-v1` or `existing-crossing-bell-v1` | `0.5.0` |
| Any modification with `relationships.crossingCoverage`, including a detection addition, a `Track.Crossings.GradeCrossingController` target component, or target `structure` `crossing-coverage-v1` | `0.5.0` |
| Any modification with `relationships.crossingEquipment` | `0.5.0` |
| Any modification with `relationships.constructionSegments`, or whose target `structure` is `bridge-construction-scenery-v1` | `0.5.0` |
| Any modification whose target `structure` is `crossing-pad-v1` | `0.5.0` |
| A modification with `Helpers.MilePost` as its target component or `milepost-v1` as its target `structure` | `0.5.0` |
| Any modification with `starterPlacement`, or whose target `structure` is `starter-placement-v1` | `0.5.0` |
| Any modification whose target `structure` is `scene-object-v1` | `0.5.0` |
| Any modification whose target has `padMarkerId` | `0.5.0` |
| A modification whose target `structure` is `child-object-v1` | `0.5.0` |
| A modification whose target `structure` is `spawn-point-v1`, or whose target has `Character.SpawnPoint` as its component and no `structure` | `0.5.0` |
| A modification with `UI.Map.MapIcon` as its target component or `station-map-icon-v1` as its target `structure` | `0.5.0` |

Fixed placement sets the base minimum. Other content can raise it as shown in the table, even if the target isn't loaded or its capability is unavailable on this build. The legacy Sylva location sign doesn't raise the pattern-based location sign dependency minimum.

Save writes `crankyant/scene-patcher >=<minimum>` without an upper bound. It keeps the entry's other constraints and does not lower a minimum. If a constraint already requires that version or a later one, Save leaves the entry unchanged. If an existing constraint excludes the required minimum, Save reports the conflict and does not write the entry. Runtime reporting adds no scene patch file, schema or capability requirement.

If a target's `structure` names a capability incompatible with the running game build, the modification fails with that reason. Other modifications still apply. The patcher records failures during target preflight or a change for the current load and doesn't retry them during that load. If it can't verify cleanup after a change, it blocks later modifications that resolve to that target.

## Failure isolation

The runtime handles failures according to when they occur and whether cleanup succeeds:

- The runtime skips a whole scene patch file if it cannot read the file, its `formatVersion` is unsupported, it has a repeated property name, its `modifications` value is not an object keyed by modification ID, or its scene target map is outside the loaded map context. None of the file's modifications runs.
- A modification fails before changing anything if it has an invalid value, its target resolves to no object or several objects, or a prerequisite fails. The object keeps its state. Every modification that lists the failed modification in `dependsOn`, directly or through another modification, fails as a dependency failure without changing anything. Modifications that do not depend on it still apply.
- If a modification fails after a change starts, the patcher restores the state captured before changing anything, including relationships and detection additions, and checks the restoration.
- If that check fails, the report names the remaining or uncertain state, such as a position that differs from before or owned spans that remain registered. Later modifications of the same object fail closed until a later world load rebuilds the object.

Changes applied before a failure stay in place, and gameplay continues. The runtime does not retry a failed modification while that world stays loaded. The next world load reads and evaluates every scene patch file again. In the native map editor, **Recheck** compares the object with the scene patch file and lifts the block once they agree. See [retry after a failed edit](editing-placement-in-the-map-editor.md#retry-after-a-failed-edit). Failure handling adds no JSON fields or format version.

## Application on each peer

Format version 1 has no multiplayer fields. The host and every client read `Data/scene-patches.json` from their own installed copy of the mod and apply it to their own scene. Each peer checks `sourceMap.mapId`, target identity and capability prerequisites. `placement.position` is in map coordinates and is converted through each peer's own world origin.

A repeated world-load callback reconciles the stored placement without accumulating offsets or retrying failed modifications. A later load of the same map, including a reconnect or re-entry into that map, starts a new load attempt. If a target still resolves under its identity, it keeps its applied state and any block due to incomplete cleanup. If the game rebuilt it, the patcher applies the modification as new. The patcher removes only its owned state from the old object, such as a detection addition's spans.

Failed modifications are retried once per load attempt. A cleanup failure is reported on that peer only and retried at the next load. Modification keys and addition IDs are the persistent identities across load attempts. In single-player, loading the same save again in one process rebuilt every modded object and added object, and each modification was applied once. This behavior hasn't been checked in a multiplayer session, for any kind of target.

Crossing changes follow the same rule on every peer. In the code of Railroader 0.6.3, a crossing controller computes its detection in each player's own game and sets its light, gate, and bell keys from it. Whether those keys also reach other players hasn't been determined.

A player who leaves, loses the connection, or joins another session returns to the main menu, which unloads the map. A later join or reconnect is therefore a new world load on that peer. The patcher builds the mod's detection additions, lights, gates, and bells once in the new world, using the scene patch file's `crossingCoverage` addition IDs and `crossingEquipment` IDs. It also applies the circuits and the modifications of existing masts and bells once. Everything it built in the earlier session disappeared with the unloaded map, including each added bell's audio.

A crossing modification that fails on one peer is reported on that peer only. The other modifications on that peer and all other peers keep their state. This behavior hasn't been checked in a multiplayer session.

## Minimal example

```json
{
  "formatVersion": 1,
  "sourceMap": { "mapId": "example-map-id" },
  "modifications": {
    "place-object": {
      "target": {
        "scene": "BushnellWhittier-MapManager",
        "path": "World/Signs/Loc: Sylva Int",
        "component": "Helpers.Culling.RendererCuller",
        "structure": "location-sign-v1"
      },
      "placement": {
        "mode": "fixed",
        "position": [12.0, 2.0, 3.0],
        "rotation": [0.0, 0.0, 0.0, 1.0]
      },
      "dependsOn": []
    }
  }
}
```

## Transform gizmo editing

A completed [transform gizmo gesture](using-transform-gizmos.md) writes the same fixed placement as numeric editing. Position stays in map coordinates and rotation uses quaternion order. Preview frames are temporary and don't appear in the scene patch file. The gesture creates no logical binding.

For an object that follows a track, the editor stores the result as a new `offset` and `rotation` relative to the same track. The native map editor's Save writes the edited mod's canonical scene patch file and dependency metadata. Undo and Redo reverse completed gestures through native history.
