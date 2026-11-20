<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->
# Scene patch file v1

This reference defines the v1 JSON fields and their meaning. Each supplying mod keeps one scene patch file at `Data/scene-patches.json`. The file uses strict UTF-8 and accepts a leading byte-order mark. The runtime only reads the file. The native Map Editor's **Save** writes it for the edited mod. See [Write a scene patch file](writing-scene-patch-files.md) for manual editing and [the modder manual](modder-manual.md) for editing in Railroader.

Current native target support covers:

- Independent boards ([move a board with its terrain effects](moving-boards.md)).
- The Ela bridge and its colliders ([move or disable a bridge with its collision](placing-bridges.md)).
- Baked scenery groups ([move or disable a baked scenery group](moving-baked-scenery-groups.md)).
- Baked scenery parts ([move or disable a single part](moving-baked-scenery-groups.md#move-or-disable-a-single-part)).
- Generic track markers ([repair, replace or detach a track-marker binding](repairing-track-bindings.md) and [give a marker a replacement ID](repairing-duplicate-marker-ids.md)).
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

The Current native targets section describes the native observations and their limits. The runtime capability classification remains `Unchecked`.

## Scene patch file fields

| Field | Type | Requirement |
| --- | --- | --- |
| `formatVersion` | integer | Required. Must be `1`. Read before anything else: a scene patch file whose version is missing, not the number `1` or newer applies none of its modifications and is protected from overwrite, whatever the rest contains. |
| `sourceMap` | object | Required. Must contain `mapId`. |
| `sourceMap.mapId` | string | Required and non-empty. It must identify the selected map definition or one of its resolved map layers. |
| `modifications` | object | Required. An object keyed by modification ID. Each key is non-empty and not only whitespace, and each value is an object. |

If a property name repeats in any object, at any depth, parsing rejects the scene patch file. This includes repeated modification keys, circuit IDs and addition IDs.

The engine applies a scene patch file only when its `sourceMap.mapId` matches the identifier of the selected map definition or a resolved map layer. If it does not match, the engine rejects the file before resolving targets or making changes. The game skips a scene patch file that names a map patch in a save, because a map patch is never the loaded map or one of its layers. The native map editor also refuses that file, even when you are editing the map patch. Its diagnosis names the bottom map layer to use instead.

When Save creates a scene patch file, it sets the scene target map to the edited map definition's bottom map layer. This is the lowest layer the game composes for the definition, including layers inherited through other layers. If the definition has no map layers, Save uses the definition itself. When you edit a map patch, Save uses its bottom map layer, never the map patch itself.

For example, a map definition that builds on `rr/bushnell-whittier` gets a file that names `rr/bushnell-whittier`. That file applies to a save of the base-game map, where the mod's native changes arrive through its map-changing script or map patch even though the mod's own map definition is not loaded. It also applies to every layered map that builds on the base-game map, including the mod's own map.

The patcher keeps an existing file's scene target map, even on Save. To apply a file only on your own map, set `sourceMap.mapId` to your map definition's identifier by hand. **Copy ID** copies the scene target map from the edited mod's file, or the scene target map that Save would write if the file does not exist yet. If the file cannot be read, **Copy ID** is unavailable and its tooltip gives the reason.

## Modification fields

The ID of a modification is its key in `modifications`. A modification has no `id` member. A modification that carries one fails as unsupported content, with a diagnosis that names its key and says that the ID is the key; the engine preserves it and still applies the other modifications.

| Field | Type | Requirement |
| --- | --- | --- |
| `target` | object | Required. Must contain `scene`, `path`, and `component`. Optional `structure` is a string. Optional `padMarkerId` is a non-empty string that names a crossbuck whose path other crossbucks share; see [Baked scenery group](#baked-scenery-group). |
| `markerId` | string | Optional. The replacement ID of a generic track marker. Requires patcher 0.5.0 or later; see Replacement ID below. |
| `placement` | object | Optional. A fixed placement contains `mode`, `position` and `rotation`; a track-following placement contains `mode`, `track`, `offset` and `rotation`. See Placement below. |
| `active` | boolean | Optional. `false` disables the object, `true` enables it. Generic activity requires patcher 0.2.0 or later; track-marker activity requires 0.5.0 or later. |
| `relationships` | object | Optional. Explicit relationship settings; see Relationships below. Requires patcher 0.5.0 or later. |
| `starterPlacement` | object | Optional. Sets a starter placement marker's track location, whether its starting cars are a wreck, or both. See Starter placement below. Requires patcher 0.5.0 or later. |
| `dependsOn` | array of strings | Required. Each value names another modification in the same scene patch file. |

The target object accepts only `scene`, `path`, `component`, and optional `structure` and `padMarkerId`. All required target strings must be non-empty, and so must `padMarkerId` when present. `path` is always the object's hierarchy path. `padMarkerId` stands beside it and is accepted only for a crossbuck whose path other crossbucks share; every other target refuses it with the reason, before anything changes. The scene adapter must resolve the identity to exactly one object. Zero or multiple matches fail the modification.

The order of keys in `modifications`, members of any object and values of `dependsOn` has no meaning. The engine applies each modification after every modification its `dependsOn` names. It applies modifications that `dependsOn` does not order in the ordinal order of their keys, defined under Canonical order below. The scene patch file therefore applies the same way regardless of its text order. A repeated `dependsOn` value counts once. If a dependency fails, its dependent modification is not applied; modifications that do not depend on it still apply.

In the native map editor, an edit cannot remove a modification while another modification names it in `dependsOn`. The refusal names those modifications. You also cannot edit a modification that does not take effect because a dependency is missing, fails or forms a cycle. The refusal gives the reason.

A modification holds the desired state of one object and must contain `markerId`, `placement`, `active`, `relationships`, `starterPlacement`, or a combination. An omitted member leaves that part of the object untouched by this mod. An explicit `active` value is applied on every application, so a later activation by the game does not survive the next application of a scene patch file that disables the object. Patcher 0.1.0 rejects a modification that contains `active`; the dependency minimum below prevents that combination.

The [scene object inventory](inspect-scene-objects.md) supplies observed identifiers and placement without changing this format. An inspection-only entry does not enable a new v1 operation. The engine rechecks the resolved object and map context immediately before changing anything. A missing, ambiguous or replaced target fails rather than redirecting the operation. Editing integrations can bind a complete scene patch file edit to its inspected observations through an in-memory token; that token is not serialized or distributed.

[World selection](select-world-targets.md) confirms scene-object identities for the existing `target` fields. Segment and node choices record their native IDs and loaded map context in the editing session. Version 1 stores fixed and track-following placement, activity, track-marker relationships and crossing coverage. A pending choice or confirmed track or node identity stays in the editing session. Applying an edit and using the native map editor's Save writes the mod values.

## Replacement ID

`markerId` gives a generic track marker a replacement ID. The game registers a track marker's location under its marker ID. When more than one scene marker holds a marker ID, that ID is a duplicate marker ID: a change to one holder removes the registration of every holder. A replacement ID gives the target its own ID, so its registration no longer shares that ID with the other holders.

The member is a string. Omitting it leaves the marker's ID as the loaded map gives it. The member has no detach form: `null` and every other value that is not a string fail the modification.

The member applies to these targets only:

- A generic track marker, with the target structure `track-marker-v1`.

Any other target, such as a crossing pad, a milepost, a starter placement marker or an object that is not a marker, fails the modification before it changes anything, with `Only a generic track marker takes a replacement ID`. The object's other modifications still apply.

The value must follow these rules, which the engine checks before it changes anything:

- It is not empty or only whitespace, it does not begin or end with a space, and it does not begin with `passengerStop:` or `flare:`. The game uses these prefixes for its own track registrations.
- It is not in use when the modification applies. A value that another scene marker holds, that a graph registration of an object that is not a scene marker holds, or that a map object of the map context uses fails the modification, and the refusal names that user. The check reads the world at the time of application. While the game has not loaded the map object manager, every value fails the modification, with a message that asks you to try again after the map has loaded. A value that an earlier scene patch file in load order has already given to a marker fails the later modification, and the earlier modification stays applied.
- The same value in two modifications of one scene patch file fails both of them with `is used by more than one modification`. The value names one marker in the file.

The replacement ID applies before the marker's `relationships.trackMarker`, its track-following placement and its `active` value, in the same modification. One modification can therefore give the marker an ID and then enable, rebind or track-follow it. Without the ID, the duplicate marker ID refuses those settings for every holder. Fixed `placement` is not affected by the duplicate marker ID.

A holder that keeps the shared ID needs `dependsOn` for a modification that sets its activity, `relationships.trackMarker` or track-following placement, when other holders of the same ID take replacement IDs in the file. The modification names the keys of those other replacement-ID modifications, so that a load applies the replacement IDs first. Without them, the change can apply while the ID is still shared, and the duplicate ID refuses it. Scene patch editing writes these keys when it makes the edit; a hand-written file must name them. A modification that another modification names in `dependsOn` cannot be removed, and its replacement ID cannot be cleared, while the dependent modification stays. For example, when `repair-yard-limit` gives the yard limit its replacement ID, the modification of the speed sign that keeps `Mg6m` reads:

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

Applying the ID moves the marker's own registration to the new ID when the marker is active and its binding resolves. Every other holder of the old ID keeps its registration. A failed modification, a removal of the modification and the removal of the member restore the effective underlying ID, computed from the loaded map and the remaining values of the file, together with the registration that goes with it.

The member requires patcher 0.5.0 or later; see [dependency minimum](#dependency-minimum). Format version 1 does not change. The engine does not check Lua scripts that look a marker up by ID, such as those that call `NativeTrack.GetMarkerLocation`, or third-party mods that look a marker up by ID. A run on the base map in Railroader 0.6.3 build 26740326, with the patcher at commit `325ad5f`, observed the game register a repaired marker under its replacement ID while the other holders kept their registrations, also after Save and a world reload. For the editing steps, see [repair a duplicate marker ID](repairing-duplicate-marker-ids.md).

## Placement

`mode` selects the placement. Any other `mode` value makes the modification unsupported; the engine preserves and does not execute it. Each target type defines the meaning of optional `structure`, as described under Current native targets. Placement is separate from logical binding: placing an object never changes its track relationships, and a relationship never moves the object. A track-following placement keeps its track reference inside `placement`, never in `relationships`, so changing one leaves the other unchanged.

A fixed placement accepts only `mode`, `position`, and `rotation`. `mode` is `fixed`. `position` must contain three finite JSON numbers in map coordinates. `rotation` must contain four finite JSON numbers, representing a nonzero quaternion. The board and bridge adapters require a unit quaternion and perform the game map/world coordinate conversion.

A track-following placement accepts only these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `mode` | string | `trackFollowing`. |
| `track` | object | Required. A segment location with exactly `segment`, `end` and `distance`, with the same rules as a `trackMarker` reference, or an endpoint with exactly `node`, a non-empty track-node ID. |
| `offset` | array | Required. Three finite numbers in metres, in the track's frame. |
| `rotation` | array | Required. Four finite numbers, a nonzero quaternion applied after the track's rotation. |

The engine reads the track's native pose in map coordinates when it applies the modification. For a segment location this is the track graph's position and rotation at `distance` metres from the named end. The frame's forward axis (+z) points along the segment away from that end: for `end` `a` towards end `b`, for `end` `b` towards end `a`. For an endpoint the frame is the node's own native position and rotation. The object's position is the track position plus `offset` turned by the track rotation; its rotation is the track rotation followed by `rotation`. Applying the same scene patch file again yields the same placement; offsets do not accumulate.

If the segment or node is missing, or the distance is outside the segment, the modification fails before changing anything and the object keeps its placement. The engine never substitutes another track. Each application evaluates the track's current pose. A world load after the track moves therefore places the object at its new position relative to the track, without changing the stored `offset` or `rotation`.

In the native map editor only the edited mod's scene patch file applies. The patcher also moves its following objects while their track moves; see [follow a track or endpoint](following-track.md). In ordinary play, every applicable scene patch file evaluates the track's current pose at world load.

## Relationships

The `relationships` object accepts `trackMarker`, `crossingCoverage`, `crossingEquipment` and `constructionSegments`, and must contain at least one of them. Each relationship has three settings:

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
| `distance` | number | Required. Finite, not negative, and at most the segment's length, in metres. |

A detach object contains only `detach` with the value `true`. `null`, `{}`, `false` and a detach mixed with reference fields are invalid. Unknown relationship kinds and unknown fields inside `trackMarker` make the modification unsupported; the engine preserves and does not execute it. The target must resolve to an object whose adapter supports the relationship. The patcher refuses a relationship on any other object instead of ignoring it.

The engine checks the reference before changing anything. A segment that does not exist, a segment ID that matches more than one segment, or a distance outside the segment fails the modification, and the object keeps its relationship and registration. The engine never substitutes another target.

### Crossing coverage

`crossingCoverage` requires patcher 0.5.0 or later. It is an object that holds `circuits`, `additions`, or both:

| Field | Type | Requirement |
| --- | --- | --- |
| `circuits` | object | Optional. Keyed by circuit ID: the ID of the circuit's native island span, which must name a circuit of the target crossing. Holds one entry per explicitly set circuit, and at least one. |
| `additions` | object | Optional. Keyed by addition ID. Holds one entry per detection addition, described under Detection additions below, and at least one. |

An entry under `circuits` contains either the three spans or a detach, and no member that names the circuit:

| Field | Type | Requirement |
| --- | --- | --- |
| `approachA`, `island`, `approachB` | object | Required together, unless the entry detaches. Each holds `from` and `to`, both track locations with the `segment`, `end` and `distance` fields above. |
| `detach` | boolean | `true`, alone in the entry. The crossing stops evaluating the circuit. |

The three settings apply per circuit. An omitted circuit keeps its effective coverage, and an omitted `crossingCoverage` leaves every circuit untouched. Selected tracks replace the circuit's spans with spans the patcher owns, covering the route from `from` to `to`; `from` is the native lower end, which lies next to the island for each approach. A detached circuit leaves the crossing's evaluation. In both cases the circuit's native spans stay registered and unchanged, so a still-valid old route stops triggering the crossing without being removed. Circuits keep their identity and native order.

A `crossingCoverage`, `circuits` or `additions` value that is not an object, a circuit or addition key that is empty or only whitespace, and an empty `crossingCoverage`, `circuits` or `additions` object each fail the modification as an invalid definition. The engine preserves that modification and still applies the other modifications. A key that repeats inside `circuits` or `additions` is a repeated property name and rejects the whole scene patch file, as described under Scene patch file fields above.

Before changing anything, the engine checks every entry. The circuit must exist on the crossing, each segment must exist exactly once, each distance must lie inside its segment and no span may have zero length. At least one circuit must keep detection. The engine refuses a modification if its result, together with the crossing's current effective coverage, detaches every circuit. A refusal leaves the crossing's coverage unchanged.

Unknown fields in `crossingCoverage` or an entry make the modification unsupported. An entry with `detach` and span fields, `"detach": false`, an entry that is not an object and a partial set of spans are invalid.

#### Detection additions

A detection addition makes the target crossing detect explicitly selected extra tracks. The patcher builds and owns its circuit alongside the crossing's native circuits. An entry under `additions` is keyed by the addition's ID, which is unique in the scene patch file and scoped to the supplying mod. The entry contains exactly these members:

| Field | Type | Requirement |
| --- | --- | --- |
| `approachA`, `island`, `approachB` | object | All required. Each holds `from` and `to`, both track locations with the `segment`, `end` and `distance` fields above. |

The ID is the addition's persistent identity. The patcher joins it with the supplying mod's mod identifier, so two mods that choose the same ID get separate additions, and each application of a scene patch file finds the addition it built before. An ID may appear in one modification of a scene patch file. When two modifications use the same ID, both fail with that reason, whichever comes first in the file.

The three spans define the addition's tracks and detection area. The named segments are the tracks that get detection, and the distances bound the area. The engine does not infer either from track near the crossing. An entry under `additions` never contains `detach`. The addition exists while the scene patch file lists it; removing the entry deletes it.

Before changing anything, the engine checks an addition's selected tracks. Each segment must exist exactly once, each distance must lie inside its segment and no span may have zero length. An addition counts as detection for the rule that a crossing keeps at least one detecting circuit. A scene patch file result that leaves no detecting circuit or addition is refused.

For each addition the engine builds one circuit with three owned spans, registers the spans in the track graph and adds the circuit to the controller after its native circuits. Added circuits follow each other in the order of their scoped identity, which within one mod is the ordinal order of their IDs, not the order of the scene patch file text. A repeated application reuses the circuit and every span whose tracks are unchanged, and replaces the spans of a role whose tracks changed. Deleting an addition removes its circuit from the controller, unregisters its spans and destroys them. Native circuits and other additions keep their state.

### Crossing equipment

`crossingEquipment` lists the lights, gates and bells that the supplying mod adds to the target crossing. It requires patcher 0.5.0 or later. Each addition belongs to the controller targeted by the modification, and that controller drives it.

The value is an object with at least one entry, keyed by equipment ID. Each key is the addition's persistent ID and contains 1 to 64 letters, digits, `.`, `_` or `-`. IDs are scoped to the supplying mod, so another mod can use the same ID for a different addition. Within one scene patch file, an equipment ID may appear in only one modification. If two modifications use it, both fail with a message explaining the conflict. Each entry has the following members and no `id` member:

| Field | Type | Requirement |
| --- | --- | --- |
| `pattern` | string | Required. The source must meet the requirements of this supported source pattern. Version 1 supports `crossing-light-gate-v1` for a light or gate and `crossing-bell-v1` for a bell. |
| `source` | object | Required. For `crossing-light-gate-v1`, use `scene` and `path` to name the Unity scene and hierarchy path of a device loaded in the map. Both must be non-empty strings. For `crossing-bell-v1`, use only `definition`, a non-empty string identifying a bell definition in the content library. |
| `placement` | object | Required. The fixed placement of the copy's root or the bell, with `mode` set to `fixed`, and `position` and `rotation` in map coordinates. See Placement. |

Applying a scene patch file leaves additions it does not list untouched, including those of other mods. When you edit or remove an addition in the edited mod's scene patch file, the patcher removes any addition that file no longer lists. The next load does not rebuild it. Repeated application keeps one copy or bell per scoped ID, updates its placement if needed, and rebuilds it if its `source` or `pattern` changes. Each bell keeps one player, one observer of the bell key and one loaded audio reference.

The modification fails as an invalid definition if `crossingEquipment` is empty or is not an object, a key is empty or invalid, an entry is not an object, or a pattern is missing. It also fails if a light or gate source lacks a scene or path or includes `definition`, a bell source contains anything other than `definition` with a non-empty string, or a placement is not fixed. A repeated key is a repeated property name and causes the patcher to reject the whole scene patch file. An unknown pattern or an unknown member in an entry, its `source` or its `placement` makes the modification unsupported. This includes an `id` member. The patcher preserves unsupported modifications without executing them.

Before changing anything, the engine checks every addition in the modification. Its pattern must be supported. Its source must resolve to exactly one loaded object or one bell definition in the content library, and meet the pattern's requirements for the target controller. See [rejected sources](adding-crossing-equipment.md#rejected-sources) and [rejected bell definitions](adding-crossing-bells.md#rejected-bell-definitions). A missing or ambiguous source fails the modification. This includes an object in an unloaded scene or a definition absent from the content library. The engine never loads a scene or substitutes another source or sound.

`crossing-light-gate-v1` has these requirements:

- Each lamp (`Track.KeyValueLampMaterialController`) and gate (`RollingStock.Controls.KeyValueBoolAnimator`) in the source observes the key the controller publishes as its lights or gates key.
- Lamp targets lie inside the source and use the lamp material. Each gate has an `Animator` and a clip.
- The source has at least one lamp or gate. It has no crossing controller, key-value object, track span, culler, bell player or missing script.
- The source is in the destination controller's Unity scene. The destination controller has exactly one key-value object and at most one culler.

`crossing-bell-v1` requires a bell definition of kind `Bell` in the content library. Its audio asset must resolve in that library, its volume must be a number greater than 0, and its audio indexes must be times greater than 0 in ascending order. The destination controller must have exactly one key-value object and be active.

Once the audio loads, the indexes must divide the clip into parts of at least one sample each. Audio loads after the modification has been applied. If loading fails or the indexes do not fit the clip, the bell remains built but silent. The game's log shows one warning under the supplying mod's scene patch file, with the bell's scope, ID, controller and reason. The bell then fails its check. Edits that keep it are refused; you can still remove or replace it.

An equipment modification is refused before any change while an addition of the target crossing fails its check. A modification that leaves the crossing's equipment as it is, such as a coverage change, neither checks nor touches the additions.

[Explicit snapping](snapping-to-track.md) writes a fixed placement. The editor computes a point on the explicitly selected segment's cubic curve or uses the selected node's position, preserving rotation, activity and logical binding. The graph selection and geometry checks belong to editing preflight and are not serialized. Runtime application and reload use the resulting absolute placement without querying a snap target. Snapping adds no dependency minimum; later track movement does not change the saved placement. Track-following placement is the separate mode above.

## Starter placement

`starterPlacement` controls where a cut of starting cars appears in a new company game and whether it starts as a wreck. It sets the starter placement marker's track location, the car placement's wreck setting, or both. The track location is where the front of the cut starts. Identify the marker with target `component` `Track.TrackMarker` and `structure` `starter-placement-v1`.

The object accepts only the fields below. It must contain `track`, `wreck` or both.

| Field | Type | Requirement |
| --- | --- | --- |
| `setup` | string | Required and non-empty. The identifier of the setup whose car placement reads the marker. |
| `track` | object | Optional. The marker's track location. Must contain exactly `segment`, `end` and `distance`, following the rules for a `trackMarker` reference. The parser accepts `{ "detach": true }`, but the engine refuses to apply it. |
| `wreck` | boolean | Optional. `true` starts the cars derailed, using the game's own wreck behavior. `false` starts them on the track. All other values, including `null`, are invalid. |

The cut extends from the track location towards the segment end from which `distance` is measured. With `end` set to `a`, it extends towards end `a`; with `end` set to `b`, it extends towards end `b`.

Omitting `track` leaves the marker's track location untouched by this mod. Omitting `wreck` keeps the car placement's native wreck setting. An explicit `wreck` value is reapplied every time, even if it equals the native setting. Omitting `starterPlacement` leaves both settings untouched by this mod.

A starter placement marker accepts no `placement`, `active` or `relationships`. Its object position and activity do not affect where the cars start. Its track location belongs in `starterPlacement`.

Before changing anything, the engine refuses these invalid definitions:

- A track location whose segment is missing or ambiguous. The refusal names the segment.
- A distance outside the segment. The refusal gives the distance and the segment's length.
- A detach, a `setup` other than the setup whose car placement reads the marker, or a marker read by no car placement of an active setup or by more than one. The refusal gives the reason. This also applies to a modification that sets only `wreck`.
- A location where the whole car cut, together with the cuts of the setup's other placements, fails the game's placement rule. The refusal names the car placement, its cars and length, and the game's reason.

The engine does not substitute another segment for a missing, ambiguous or out-of-range location. The other cuts stand at the track locations set by the scene patch file's other starter placements, or at their current locations otherwise. The result therefore does not depend on the order in which modifications apply; see [place a starter placement marker on the track](placing-starter-markers.md#what-the-patcher-checks-first). A refused modification leaves the marker and the wreck setting as they were, and the other modifications still apply.

If an application fails after changes have begun, the engine restores the captured wreck setting, track location and registration. Removing the modification or its `wreck` member restores the wreck setting the car placement has without this mod, recalculated from the loaded map and remaining values.

## Objects the game switches

The game switches some listed objects on and off itself, outside track-group activation. In Railroader 0.6.3 these are:

- A progression controller (`Game.Progression.ProgressionEntityObjectController`) on a town folder below `World/Large Scenery`. It sets the objects of its lists from the progression state of its industry or passenger stop, when it starts and whenever the industries change. It switches 13 station map icons, the station groups `Cochran flagstopstation`, `Alarka Depot` and `Alarka Jct Station`, and the track marker on `Hemingway flagstopstation`.
- A map feature (`Game.Progression.MapFeature`). It sets the objects of its `gameObjectsEnableOnUnlock` list from whether it is unlocked. The `signals` map feature switches the track markers `Sign Begin CTC Andrews` and `Sign Begin CTC Sylva`.
- A rail bridge's construction controller, described under [Baked scenery group](#baked-scenery-group).

Only an object that a list names directly counts. A switch of a folder above an object is no switch of that object. For an object that one of these switches names, whatever its family:

- `"active": false` is an explicit disable, which holds through the switch. The patcher points each list entry that names the object at a hidden stand-in object, so the game's later switches set the stand-in, and the object stays inactive.
- An omitted `active` leaves the switching to the game, as without the patcher.
- `"active": true`, clearing `active` and removing the modification return the object to the state that the switch gives it now, and the switch sets the object again from then on. The patcher verifies the `active` value, not the activity the switch chose, so an enabled object that progression keeps off still verifies.
- Undo and Redo of the disable keep the object consistent with the switch's current state.

For a track marker whose activity also follows a track group, `active: false` holds against both its track group and the map feature or progression controller; `active: true` stays refused, as under [Markers with group-driven activity](#markers-with-group-driven-activity).

In the native map editor, the **Object** tab and the footer's message area show an amber warning for an explicitly disabled object that a progression controller or a map feature switches: `This object stays off in the game although the game's progression switches it on: your disable holds against it. Click Enable or Clear activity to let progression switch it again.` for progression, and `This object stays off in the game although unlocking a map feature switches it on: your disable holds against it. Click Enable or Clear activity to let the map feature switch it again.` for a map feature, and `This object stays off in the game although progression or a map feature switches it on: your disable holds against it. Click Enable or Clear activity to let them switch it again.` for an object that both switch. For a track marker whose activity follows a track group, where `active: true` is refused, the warning reads `Click Clear activity` in place of `Click Enable or Clear activity`. An object without an explicit disable, or one that the game does not switch, shows no such warning. While the disable holds, the **Dependencies** section keeps naming the progression controller or map feature that switches the object. While no explicit disable holds the object and the game switches it off, **Enabled** on the **Object** tab reads `Yes; the game switches it off now`.

For a station group or part, a station map icon, a track marker, a milepost or crossing pad included, and an existing crossing mast or bell, a disable that could not hold is refused before anything changes, with the reason, whether it comes from the native map editor, a hand-written scene patch file or an import, and the modification fails; the file's other modifications still apply, and enabling stays possible. That is the case for an object that progression or a map feature switches while the patcher cannot share its activity in this load (`The patcher cannot share this object's activity with the game's progression or map feature in this load, so a disable would not hold. This object cannot be disabled`), and for an object that a progression controller or map feature names while the game's members of that switch are unavailable in a build (`The game's progression controller members are unavailable, so a disable of this object would not hold against progression. This object cannot be disabled`, or the same for `map feature members` and `its map feature`). The Player log also names the missing members. In the native map editor, **Disable** is then unavailable with the same reason. None of this has been observed in Railroader yet.

## Several mods

During ordinary play the runtime considers each loaded mod that has `Data/scene-patches.json` and lists `crankyant/scene-patcher` under `dependencies.required` in its `Info.json`. It processes those scene patch files in Railroader's load order, the order of the game's loaded mod list. Map-layer order plays no part, and the scene patch file has no priority field. A scene patch file whose `sourceMap.mapId` matches neither the selected map definition nor one of its resolved map layers is skipped, and the patcher is expected to add a game warning for it. The warning names the supplying mod, its identifier and version, the map the file is for, and the loaded map and its layers. It doesn't ask for a report, because the file is meant for another map. Other scene patch files still apply. The Player log has one line per scene patch file for each load attempt, such as `... map rr/bushnell-whittier (layers none): My mod [me/my-mod] scene patch file for rr/bushnell-whittier: 2 of 2 modifications applied, 0 failed, 0 unchanged`, or `... scene patch file for me/my-map skipped: not the loaded map or one of its layers`. In the native map editor only the edited mod's scene patch file is applied, as described above.

A failed scene patch file or modification does not undo earlier scene patch files or stop later ones. An unreadable scene patch file or `Info.json` skips that mod as a whole.

When scene patch files from different mods modify the same existing target, the runtime adds one informational game message for that object. It names the object as `scene/path [component]`, lists the mods and their identifiers in load order, and states any declared dependency among them. Another mod modifying the same object updates the message. The message reports a possible incompatibility, not a confirmed conflict. Application continues in load order, no change is undone, and the later scene patch file's values are applied last. A declared dependency does not suppress the message. There is no message for a target that resolves to no object or several objects, or for modifications from one mod alone. The message is not a failure report and adds no JSON fields.

## Runtime failure reports

If a scene patch file or modification fails at runtime, a structured warning appears in the game, separate from the historical file log. A modification warning identifies the source as Railroader Scene Patcher and includes the supplying mod name, identifier and version, `Data/scene-patches.json`, operation, target when resolved, error, patcher version and game version. The warning labels the supplied mod author as `Mod author` and includes an HTTP or HTTPS website when present. It does not invent contact details.

Invalid supplied definitions are reported to the mod author. Unknown failures and demonstrated patcher faults are reported to the patcher maintainer. The runtime reports a given source and operation once per loaded world. Invalid or unsupported scene patch file content that requires recovery remains an editing matter; this reporting behavior adds no JSON fields or format version.

## Current native targets

### Independent board

The patcher recognizes independent boards. The target `scene` and `path` name the board object, its `component` is `Helpers.Culling.RendererCuller`, and `structure`, when present, must be `independent-board-v1`. The object must carry that culler and exactly one `Map.Runtime.MaskComponents.CircleMapMask`, both enabled, with active parents. The patcher refuses a mask that flattens terrain height and a hierarchy with a `Track.*` component, another culler, another mask component, another culling handler or a missing script. The board itself may be disabled through `active`. The patcher requires the map conversion, culling, mask, terrain tile and terrain completion functions it uses. A board placement has no track binding.

After setting the board placement, the patcher requests a culling-position update and rebuilds the circle map mask. It then verifies that the culling sphere and every mask registration are at the board's position, or, for a disabled board, that neither registration remains; a mismatch fails the modification and restores the captured placement, activity and registrations. The game regenerates terrain output at the old and new positions. The patcher reports each position complete after a tile-built notification covering it and idle terrain work with no pending invalidations. A position whose terrain tile is not built needs no rebuild. A position that has not completed after 30 seconds is reported with the completed and outstanding positions; the modification stays applied. Save does not write the generated terrain output; the game regenerates it from the saved placement. Native edited terrain tiles are separate and are not changed by a board modification.

If a change fails and restoration is verified, later modifications of the same board fail until the restored terrain output completes. If that output stops, or restoration cannot be verified, they fail for the rest of the load.

An isolated native test on Railroader 0.6.2 build 26335745 observed the rendered Sylva board, updated culling sphere and registered mask at the desired placement, with settled terrain output; its sampled tree regions were empty. A separate research trial on the same build moved the Sylva board's mask onto one generated tree, observed the tree leave the generated tile output and return after restoration. Other boards qualify through the component pattern without individual trials.

The Current native targets section lists all supported object families. Families not listed there are not implemented.

### Bridge

The patcher recognizes the Ela bridge root at `World/Large Scenery/Ela/Ela Bridge`, with target `component` `Helpers.Culling.RendererCuller`. Set `structure` to `bridge-colliders-v1`; the native map editor's Save writes it. Use the `scene` value from the inspected identity. A modification may set `placement`, `active`, or both, as for the board.

The patcher moves and enables or disables the bridge root. Its colliders and renderer cullers move with it; the patcher refreshes the culling position of every active culler in the hierarchy and synchronises the physics scene. The parents of the root and its root culler must be active, the hierarchy must contain at least one collider, no rigidbody and no track-owned component, and no construction-scenery entry may switch the root. Before changing anything, the collider identities, shapes, enabled flags and positions relative to the root must equal their first observation in the load.

After a change the patcher verifies the root placement and activity, the collider layout, collider activity under the root, and that the loaded generated trestles within 250 m of the bridge and the construction-scenery controller that references the bridge are unchanged. Separate track-owned scenery is never part of the moved hierarchy. A difference fails the modification and restores the captured bridge state; an unverified restoration blocks further operations on the bridge for the load. See [move or disable a bridge with its collision](placing-bridges.md) for editing instructions and limits.

### Station map icon

The patcher recognizes the station map icons of the Station map icon family: each object with a `UI.Map.MapIcon` component directly in `World/Map Labels/Station Icons`. The target `scene` and `path` name the icon's object, its `component` is `UI.Map.MapIcon`, and `structure`, when present, must be `station-map-icon-v1`. The native map editor's Save writes the structure. A map icon elsewhere, deeper in the folder or such as one the game creates for a car or a player while it runs, does not resolve. The `Station Icons` folder is no group root, and neither the folder nor any other object in it is listed. A modification may set `placement`, `active`, or both.

The patcher moves the icon's object and enables or disables it. The map draws the icon where its object stands. In Railroader 0.6.3, a moved Bryson icon appeared 300 m east of its original position in play. By the game's code, an icon registers with the map when it is enabled and leaves it when it is disabled, through its own lifecycle (`UI.Map.MapIcon.OnEnable`, `OnDisable`; `UI.Map.MapBuilder.Add`, `Remove`). The station's track span, depot house and station agent are native map data in `Map.json`; no modification of an icon changes them. Before changing anything, the icon's scene must be loaded, its hierarchy must hold no missing script, rigidbody or culler, and its map coordinates must be readable. A culler refuses only the icon whose hierarchy holds it.

After a change, the patcher verifies the icon's placement and activity, that it stands in the same object as before, and that the map has the icon registered while it is active with its parents; before the map exists, registration is not checked. A difference fails the modification and restores the captured placement and activity; an unverified restoration blocks further operations on the icon for the load. The game's progression switches 13 icons on or off with their station at load and when industries change; `"active": false` holds through that switch, as [objects the game switches](#objects-the-game-switches) describes. See [move or disable a station map icon](moving-station-map-icons.md) for editing instructions and limits.

### Baked scenery group

The patcher recognizes the group roots of the Baked scenery group family. The target `scene` and `path` name the group root, its `component` is `UnityEngine.Transform`, and `structure`, when present, must be `baked-scenery-group-v1`. The native map editor's Save writes the structure.

A group root is a scene object one level below a folder of `World`, or, below `World/Large Scenery`, one level below a town folder. The folder `World/Large Scenery/Almond Bridge` is itself a group root: it holds the construction of one rail bridge, and its `Abutments` and `Steel` are parts. A folder whose children are crossings, or crossings and road bridges, is not a group root; its children are. To be listed in this family, a root must be serialized by the scene, must not be a `Map.json` object or listed by another family, and must hold a renderer, collider, light, storage pile (`Scenery.Pile`) or log pond (`Scenery.LogPond`) at or below it on an object the scene saves. A candidate without one is not listed at all. A collider the game builds while the camera is near, as a storage pile does, does not count; the pile itself does.

A group root that holds a car loader (`RollingStock.CarContentSwapper`) at any depth is not listed, and neither is anything below it. The game loads Robinson Gap coal by teleport between the industry's track spans, not at `World/Large Scenery/Robinson Gap Mine/Coal Loader`, so moving that loader would not move loading. A group or part modification whose path names such a group root fails before any change, with that reason, and the other modifications still apply.

The family leaves out a group holding any of the following at any depth: a track marker, station click spot, spawn point or map icon. It also leaves out a group that holds a track construction controller (`Game.Mow.TrackConstructionScenery`) but is no rail bridge construction. Those objects are inspection only. A group with a spawn point is not moved as a whole, because its spawn points are edited one by one in the [Spawn point](#spawn-point) family.

Rail bridge constructions outside Ela are groups of this family under Bridge / Rail bridges: `World/Large Scenery/Bryson/Bridge 1`, `World/Large Scenery/Almond Bridge` and each plate girder bridge below `World/Large Scenery/Plate Girder Bridge`. The track they carry is native map data and keeps its own trestle or steel flags; a modification moves only the construction. The station groups `Cochran flagstopstation`, `Alarka Depot` and `Alarka Jct Station` are switched by their progression controllers; `"active": false` holds through that switch, as [objects the game switches](#objects-the-game-switches) describes. Bryson Bridge 1 and the Almond Bridge carry a construction controller (`Game.Mow.TrackConstructionScenery`). It switches their parts on and off by the construction state of the track segments it names by ID, its construction segments. `"active": false` holds a group root or part off even when the controller would switch it on; without it, or with `"active": true`, the controller switches the object. A construction's `placement` must use `"mode": "fixed"`: a track-following placement of a rail bridge or of one of its parts fails before changing anything. The patcher reads construction segment IDs without resolving them, so a segment that no longer exists in the track graph causes no patcher failure. In Railroader 0.6.3, after a construction segment of Bryson Bridge 1 was removed, the parts stayed visible and the game reported an error on world load. See [Construction segments](#construction-segments).

Road bridge constructions are groups of this family under Bridge / Road bridges: every group root whose name contains `Road Bridge`, below `World/Large Scenery` and below the `World/Roads` folders, including `World/Roads Bryson/US19 Gov Island/Concrete Road Bridge - 60m`, which stands in a folder beside a grade-crossing setup. The road they carry is native map data; a modification moves only the bridge. A road bridge's `placement` must use `"mode": "fixed"`: a track-following placement of a road bridge or of one of its parts fails before changing anything, with `Road bridges use fixed placement. Track-following placement does not apply to them`. Each road bridge holds a renderer culler and a scene ground mask on its root; see [scene ground masks](#scene-ground-masks).

Each crossbuck of an unprotected crossing is a group of this family under Grade crossing, wherever its crossing stands below `World`. Its root is the crossbuck, such as `World/Roads Andrews/Crossing Unprotected - Robbinsville/Sign-Crossing0 (1)`; its model and LOD levels are no parts. A crossbuck takes a fixed `placement` and `active`. The crossing pad and whistle signs beside it keep their own families and do not move with it. Where unprotected crossings share one path, as the three crossings below `World/Roads Bryson/Old River Rd` do, their crossbucks share their hierarchy path, such as `World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (1)`. The target then names a crossbuck by that path and the target member `padMarkerId`, the marker ID of its crossing's pad (`Mbkd`, `M0sh` or `Mgdu` there). Without `padMarkerId`, the shared path names every crossbuck at it, and the modification fails as ambiguous. A `padMarkerId` that matches no pad at that path names none and fails too. A crossbuck whose path names it alone refuses `padMarkerId` with `This crossbuck's path names it alone, so its target takes no padMarkerId. Remove padMarkerId from this target`, and every other target refuses it with `padMarkerId identifies only a crossbuck at an unprotected crossing whose path other crossbucks share. Remove padMarkerId from this target`. A refusal changes nothing, and the other modifications still apply.

```json
"target": {
  "scene": "<scene from the inspected identity>",
  "path": "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (1)",
  "component": "UnityEngine.Transform",
  "structure": "baked-scenery-group-v1",
  "padMarkerId": "Mbkd"
}
```

A path that names no listed group root, or more than one, fails the modification before changing anything. A modification may set `placement`, `active`, or both, and for Bryson Bridge 1 and the Almond Bridge also `relationships.constructionSegments`; see [construction segments](#construction-segments).

The patcher sets the root's physical placement and its own activity; everything below the root moves with it, and the activity of the parts stays theirs. Placement is in map coordinates and converted through the current origin as for other objects below `World`. After a move the patcher refreshes the culling sphere of every enabled renderer and game-object culler in the group, moves the sphere of every simple culler, and synchronises the physics scene. A disabled group's renderer cullers release their spheres; a simple culler on the root stops switching the root while the group is disabled and resumes when it is enabled. The group must hold no missing script and no rigidbody.

After a change, the patcher verifies the root's placement and activity. It also checks each part's placement relative to the root, or in map coordinates if the part has its own fixed placement, and each part's own activity unless a culler switches it. For a root or part that a construction controller switches, the patcher verifies its `active` value, not the activity the controller chose. Every enabled culler's sphere must stand at its culler. For a rail or road bridge construction, every collider must keep its shape and enabled state, and the physics bounds of a box, sphere or capsule collider must follow its transform. For a rail bridge construction, the generated trestles within 250 m of the bridge must also stay unchanged. A rail bridge construction that holds a `Track.*` or `AutoTrestle.*` component is refused. A difference fails the modification and restores the captured group; an unverified restoration blocks further operations on the group for the load. See [move or disable a baked scenery group](moving-baked-scenery-groups.md) for editing instructions and limits.

### Baked scenery part

The patcher recognizes parts of groups in the Baked scenery group family. The target `scene` and `path` name the part, its `component` is `UnityEngine.Transform`, and `structure`, when present, must be `baked-scenery-part-v1`. The native map editor's Save writes the structure. A part is a scene object at any depth below a listed group root, rather than a renderer or component. The scene must serialize it, and it must have a renderer, collider, light, storage pile or log pond at or below it, unless it holds a scene ground mask (see [scene ground masks](#scene-ground-masks)). Groups in the Inspection only group have no parts. Siblings with the same name under the same parent, and objects below them, are not parts. An object that carries a `KeyValueBoolAnimator`, and everything below it, is not a part: the game animates it. A culling container, an object below the group root that carries a simple culler (`Helpers.SimpleCuller`), has no renderer, collider, light, storage pile or log pond of its own and holds other objects, is not a part; the objects in it are, and their paths include the container. A modification whose path names a culling container fails before any change, with that reason. A LOD group level belongs to the object holding the LOD group and is not a part. An object listed by another family is not a part.

A path that names no part or more than one fails before changing anything. A modification may set `placement`, `active`, or both. The patcher refuses `active` before changing anything for a part that a game-object culler switches by camera distance. The culler would override a stored setting, so the patcher cannot maintain it and never changes that part's activity. For a part that a rail bridge's construction controller switches, `"active": false` holds the part off against the controller.

`placement` is fixed in map coordinates, rather than relative to the group. Everything below the part moves with it; the group root and other parts keep their placement and activity. A part with a fixed placement keeps it when its group root or a part above it moves later, regardless of scene patch file, key order or load order. The group placement applies first, followed by each fixed part placement. A part without a placement modification keeps its place relative to the object directly above it, whether the group root or another part. Removing a part's modification in **Scene patches** returns it to its unmodified placement relative to that object at the object's current position.

After a change, the patcher verifies the part's placement and activity. It also checks that the group root and other parts kept their placement and activity, and that objects below the part kept their placement relative to it. It refreshes every enabled culling sphere in the group, and every culler keeps the radius the scene gives it. A part moved outside the sphere of a culler above it keeps its own culling sphere, centered on the part's renderer, collider and light bounds and enclosing them. The patcher registers that sphere with the game's culling manager and removes it when the part is back inside the culler's sphere, is disabled, or loses its modification. A part that was never moved gets no sphere of its own. The scene patch file holds no field for own culling spheres. A difference fails the modification and restores the captured part; an unverified restoration blocks further operations on the part for the load.

#### Storage piles and industry buildings

The storage displays `Andrews Snowbird Paper`, `Sylva Paperboard` and `Connelly` below `World/Large Scenery` are baked scenery groups, and each storage pile in them is a part, such as `World/Large Scenery/Whittier/Connelly/Log Pile L1a` or `World/Large Scenery/Andrews/Andrews Snowbird Paper/Pulpwood Piles/Pile (3)`. Target them with `baked-scenery-group-v1` and `baked-scenery-part-v1`, as any group and part. A pile shows the storage of its industry through a `Scenery.BulkIndustryStorageSynchronizer`, which names the industry component and the load by ID. Neither `placement` nor `active` changes that storage.

After a change, every enabled pile in the group that stands elsewhere than before moves its own culling sphere to where it stands, and such a pile that shows its logs lays them out again there; a pile that the camera has culled lays them out when it shows again. A sphere that does not stand at the pile's new place with the pile's radius fails the modification, which restores the group or part. The game builds a pile's collider child while the camera is near; the scene does not save it, it is no part, and it moves with the pile. A group with a pile is refused before any change when the game's pile members that this refresh uses are unavailable.

Industry positions, track spans and their delivery and loading points are native map data in `Map.json`. No modification targets them, and moving a decorative industry building or a storage display leaves them in place, so loading and unloading on the native spans are unchanged.

#### Lake surfaces

A lake surface is a `LakePolygon` with its water mesh, saved in the scene. The groups `World/Large Scenery/Whittier/Whittier Log Pond`, `World/Rivers/Fontana Lake (1)` and `World/Rivers/Fontana Lake (2)` hold one each. Target them with `baked-scenery-group-v1` and `baked-scenery-part-v1`, as any group and part. The parts of `Whittier Log Pond` are `Whittier Saw Mill Log Pond`, the water mesh, and `LogPond`, a `Scenery.LogPond` that lays out the logs of the sawmill's storage. A Fontana lake has no parts, and a renderer culler on its root.

The patcher sets the placement of the object that carries the water mesh, or of the group root above it. The lake masks and rivers in `Map.json` are native map data: they shape and color the terrain, no modification targets them, and moving a lake surface leaves them in place.

A modification that sets `placement` on a group root or part with a lake surface whose mesh renderer is part of a static batch fails before any change, because the game would keep drawing the batched water at its old place. `active` alone still applies.

The log pond shows its storage through a `Scenery.BulkIndustryStorageSynchronizer`, as a storage pile does, and neither `placement` nor `active` changes that storage. After a change, the patcher moves the log pond's culling sphere and has it lay out its logs again where it stands, under the same checks as a pile; a log pond builds no collider.

#### Scene ground masks

A scene ground mask is a `Map.Runtime.MaskComponents` component that the scene saves. It registers a shape in the game's terrain system where its object stands. The station and depot groups hold one each: a `RectangleMapMask` on an object named `Mask`, which is a part, and the group root and any part above it name that part. Target the groups with `baked-scenery-group-v1` and their parts with `baked-scenery-part-v1`, as any group and part. The object that holds a mask is listed as a part even without a renderer, collider or light.

A placement of a group root or part that holds a mask, or that stands above one, rebuilds each enabled mask that the edit changed and that no longer stands at its holder, through the game's own `Rebuild`, so the mask registers once with its center and rotation at the holder. The patcher asks the game to rebuild the mask, which makes the game regenerate terrain and vegetation at the old and the new position. The patcher tracks that terrain output for the edited object at the mask's center. Save does not write the output. A part with a fixed placement keeps its mask where it is when its group moves. The patcher expects `"active": false` to release a mask and `"active": true` to register it again, and fails the edit when the mask does not agree with the activity. Not yet observed in Railroader.

A mask that also flattens the terrain height refuses a placement of its holder, of a part above it and of its group, before any change. The refusal also applies when the flattening mask stands on a part with its own fixed placement, although that part would stay behind. `active` stays editable. In Railroader 0.6.3 the masks of the station and depot groups all flatten the terrain height, so those groups and their `Mask` parts cannot be placed in this version. The refusal stands until a Railroader test confirms that the game restores the height after Undo and removal.

A road bridge can hold its mask on the group root: a `RectangleMapMask` as long as the bridge, which the Railroader 0.6.3 scene sets up to cut trees. A placement of the group rebuilds the mask at the bridge and tracks the terrain output for the group; a placement of a part leaves the mask where it is. The mask of `World/Roads Dillsboro/Old Home Town Road Bridge` also flattens the terrain height, so a placement of that group is refused. The mask component of `World/Roads Whittier/Whittier Road Bridge` is disabled in the scene: it holds no registration, a placement or an `active` change of that group rebuilds nothing and starts no terrain output, and the inventory names no ground mask for it. In Railroader 0.6.3, a move of Whittier Road Bridge showed no mask dependency and started no terrain output.

The Generic track marker on the root of Hemingway flagstopstation keeps its own `track-marker-v1` target, and the station is no baked scenery group. Only this station building gets mask handling in the Track marker family. Its flattening mask refuses a placement of the marker before any change, so an existing file that moves the marker fails that modification on load. An `active` change of the marker follows the mask below it the same way as a group. The masks of other marker objects, such as a sign's circle mask, are neither rebuilt nor refused.

In Railroader 0.6.3, the US19 Gov Island road bridge's tree-cutting mask moved with the bridge. Station and depot mask moves and terrain-height restoration remain unverified in play. The automated tests use a controlled mask and terrain system, and they check the rebuild, the terrain output, the flattening refusal, the Hemingway track marker, restoration, Undo, Redo, Save and reload. A mask whose shape crosses a terrain tile border is tracked on the tile of its center only, and **Retry** asks again only for that tile; boards behave the same way. A road bridge mask is as long as its bridge, up to 200 m for `Almond Road Bridge` and 60 m for the bridge at US19 Gov Island, so the tracked output can complete while a neighboring tile is still being rebuilt. Without the game's ground mask members, the groups that hold a mask are refused, but the Hemingway marker is not refused and would move without its mask.

#### Buildings with pickable toggles

The groups `World/Large Scenery/Bryson/Bryson Section House`, `World/Large Scenery/Bryson/Bryson Tower Chance Clayton`, `World/Large Scenery/Bryson/Bryson Turntable` and `World/Large Scenery/Dillsboro Turntable/Roundhouse 3` are baked scenery groups under Railroad service building. They hold pickable toggles (`RollingStock.Controls.KeyValuePickableToggle`). Target them with `baked-scenery-group-v1` and their parts with `baked-scenery-part-v1`, as any group and part.

- A lantern's toggle stands on its `TrainmanLantern` part, with the `Effects.LanternController` that switches the lantern: `.../Bryson Section House/TrainmanLantern` and `.../Bryson Tower Chance Clayton/Culling Container/TrainmanLantern`. The object that the controller's `filamentRenderer` names, the lantern's flame, is turned toward the camera and scaled every frame, so it and every object below it are no parts; it moves with its lantern.
- A stall's toggle stands on `Roundhouse Modular A Stall` below each stall, with the `KeyValueBoolAnimator` that opens its doors. That object and everything below it are no parts; the stall above it is, such as `.../Bryson Turntable/Roundhouse/Stall (2)` or `.../Roundhouse 3/Stall (1)`. A modification that names the animated object or a door fails before any change.
- The tower's name text is the part `.../Bryson Tower Chance Clayton/Culling Container/Sign-Station/Text`, a `TMPro.TextMeshPro` whose text the scene saves. `Culling Container` is a culling container and no part.

In the game code, a toggle is picked through a collider at or below its object and flips one Boolean key of the nearest key-value object (`KeyValue.Runtime.KeyValueObject`) above it; it reads no position. That key-value object carries a `RollingStock.Controls.GlobalKeyValueObject` with a `globalObjectId`; the game save keeps the key values by that ID, not by position. It stands on the group root of `Bryson Section House` and `Roundhouse 3`, on the `Roundhouse` part of `Bryson Turntable` and on the tower's culling container. In Railroader 0.6.3, the moved Bryson Section House lantern and Bryson roundhouse doors worked at their new positions. Their states survived Save & Quit and reload. `placement` and `active` never write a key, an ID or a stored value. With `active` set to `false` on a toggle's object or above it, the toggle cannot be clicked. With `active` set to `false` on the key-value object's object or above it, the game unregisters its ID, so a game save made while it is disabled might not keep the stored values. The patcher does not refuse such a modification. The turntable and the tracks into the stalls are native map data in `Map.json`; no modification targets them.

### Spawn point

The patcher recognizes every scene object with a `Character.SpawnPoint` component, such as the spawn points under `World/POIs`. Set the target `component` to `Character.SpawnPoint` and `structure` to `spawn-point-v1`; the native map editor's Save writes the structure. Use the `scene` value from the inspected identity. A path that names no loaded spawn point, or more than one, fails the modification before changing anything. A modification may set `placement`, `active`, or both.

The patcher moves the spawn point's transform and sets its activity. It has no renderer, collider or culler. In Railroader 0.6.3, a new East Whittier company game started at its moved spawn point when the map had no native spawns. The `/tp` command used the moved Alarka point. The precedence of native spawns and a company start setup and rescue remain unverified. Quick search lists active spawn points, but Railroader 0.6.3 doesn't open quick search. The patcher applies eligible spawn point modifications before the new game reads the start point. Modifications with `dependsOn` entries or a `trackFollowing` placement wait for the normal world load. After a change the patcher verifies placement and activity and restores the captured state if verification fails.

Native spawns in the `spawns` section of `Map.json` are native map data. The native map editor edits them in its **Spawns** category, and the patcher does not list or change them. See [move player spawn points](moving-spawn-points.md) for editing instructions.

### Existing crossing mast and bell

The patcher recognizes the masts and bell of an existing crossing. A mast's target `scene` and `path` identify its light object, such as `.../Grade Crossing - Bryson Everett/Signal 1/Signal 1`. Its `component` is `Track.KeyValueLampMaterialController`. If `structure` is present, it must be `existing-crossing-mast-v1`. The gate on a child of the light object is part of the mast and cannot be targeted separately.

A bell's target identifies the bell player that the crossing controller creates at `.../Crossing Bell`. Its `component` is `Audio.IntegerLoopingPlayer`. If `structure` is present, it must be `existing-crossing-bell-v1`. The native map editor's Save writes the structure for both masts and bells.

A mast matches `existing-crossing-mast-v1` when it meets these requirements:

- Its light object is below a crossing controller, and that controller's key-value object is the nearest key-value object above it.
- Each lamp and gate on or below the light object observes the `lights` or `gates` key published by the controller.
- Every lamp target is inside the mast's hierarchy.
- No gate is elsewhere under the light object's parent.
- The mast contains no second light object, crossing controller, key-value object, detection span, game-object culler, bell player, rigidbody or missing script.
- No track-relative component switches the mast with a track group.
- No other loaded object shares its hierarchy path.
- The patcher did not add the equipment.

A bell matches `existing-crossing-bell-v1` when it is the bell player created by its crossing controller and the controller is enabled. If a target matches no object, matches more than one object, or does not match its supported source pattern, the modification fails before changing anything and reports the reason. Other modifications still apply. See [refusal reasons](moving-crossing-equipment.md#refusal-reasons).

A modification may set `placement`, `active`, or both. Each is a separate operation. The modification accepts no `trackMarker`, `coverage`, `equipment` or `starter` relationship. Placement and activity changes preserve the equipment's logical binding to its crossing controller, which continues to drive the same mast or bell.

`placement` uses fixed placement in map coordinates. The mast's light and gate move and rotate together. The crossing controller, other masts and bell keep their physical placement. Without a placement modification, the mast or bell keeps the scene's placement relative to its parent. Removing the modification in **Scene patches** returns it there. A modification whose `placement.mode` is `trackFollowing` fails before changing anything and reports that track-following placement is unsupported. A bell's sound has no direction, so its rotation has no effect. Scene patch editing shows the rotation but does not let you set it.

`active` sets the activity of the mast's light object or the bell itself. The patcher applies `"active": false` on every load, and the equipment stays disabled as the game's culler hides and shows the crossing. Without that value, the equipment follows the game as before. The bell player discards its sound when disabled. When the bell is enabled again, the patcher configures the player with the crossing's loaded bell sound.

If a game-object culler lists the equipment itself, the patcher refuses `active` before changing anything because the culler would override the saved value. At the Bryson Everett crossing in Railroader 0.6.3, the crossing's culler lists each mast's parent, so `active` is supported for those masts.

A crossing's game-object culler keeps its radius. A mast moved outside that culler's sphere gets a sphere of its own, as a moved [baked scenery part](#baked-scenery-part) does. The patcher removes the listed object above the mast from the crossing's culler list and gives that object a sphere that stands in for the culler's, with the same center and radius. The mast uses its own sphere, sized around the mast and the gate arm's position at that moment. The patcher returns both objects to the game's culling when the mast returns inside the crossing's sphere, the modification is removed or restored, or the patcher's state for the world ends. No culler switches a bell, and the patcher gives it no sphere.

After a change, the patcher checks the equipment's physical placement and activity. It also verifies that the crossing controller and every other object below it kept their physical placement and activity, and that each culling sphere is where the plan requires it. A difference fails the modification. The patcher restores the captured placement, activity and culling membership. If it cannot verify the restoration, it blocks further operations on that mast or bell for the load. See [move or disable an existing crossing mast or bell](moving-crossing-equipment.md) for editing instructions and limits.

### Track marker

The patcher recognizes an object holding one Generic `Track.TrackMarker`. The target `component` is `Track.TrackMarker`; set `structure` to `track-marker-v1`. The object must meet these requirements:

- It is not a milepost or a crossing pad. A Generic marker inside a crossing setup, such as a whistle sign, qualifies.
- No other component listens to the marker's location changes.
- A `Track.TrackRelativePosition` whose activity follows a track group activates this object itself, and no other component has replaced its activation callback.
- No loaded component other than that relative position holds a direct reference to the marker. If a scan cannot read a field able to hold a marker, it refuses every marker.
- No new-game car placement uses the marker.

A modification may set `markerId`, `relationships.trackMarker`, `placement`, `active`, or any combination. See [replacement ID](#replacement-id). For a marker that a progression controller or a map feature switches, such as `Sign Begin CTC Sylva`, `active: false` holds through that switch; see [objects the game switches](#objects-the-game-switches).

A `trackMarker` setting writes the marker's serialized location and its named-location registration in the track graph: the patcher removes the registration under the marker ID, writes the location, and registers the marker at the new location when it is enabled and active. A detach leaves no listed track location. Map labels below the marker are registered with the map again, so their dimming follows the new location's track group. Placement moves the object root and refreshes its renderer cullers; it never changes the binding. Before changing anything the patcher requires one registration of this marker at its current location for an enabled, active marker with a resolvable location, and none otherwise. After a change it verifies the location, the placement, the activity, that registration rule and the map labels' track group. A difference fails the modification and restores the captured location, registration, placement and activity; an unverified restoration blocks further operations on the marker for the load. See [repair, replace or detach a track-marker binding](repairing-track-bindings.md) for editing instructions and limits.

#### Markers with group-driven activity

A marker object whose `Track.TrackRelativePosition` has its activity follow the track group subscribes to the track graph's group event for the group of its bound segment, and the game activates or deactivates the object with that group. For such an object:

- A `trackMarker` setting also moves that subscription: after the location change the patcher runs the relative position's own unsubscribe and observe steps, so exactly one subscription observes the new segment's group. A detach, or a location that does not resolve, leaves no subscription.
- `active: false` is an explicit disable. The patcher replaces the relative position's activation callback with a hold that keeps the object inactive, before anything else changes, so a later group activation cannot activate it, not even briefly, including during a repeated application.
- An omitted `active` leaves the object's activity as it is. Without an explicit disable the game's group activates and deactivates the object. An explicit disable from another modification of the same object stays in place, whether that modification is in the same scene patch file or in another mod's scene patch file.
- `active: true` is refused before changing anything, because the object's activity belongs to its track group. Omit `active` to keep group-driven activity.
- Removing the disable in the native map editor, by clearing `active` or removing the modification, releases the hold and observes the group again, so the object's activity follows the group's current state.

After a change the patcher also verifies the subscription count, the observed group and the activity the group or the hold requires. A difference fails the modification and restores the captured location, registration, subscription, hold, placement and activity; an unverified restoration blocks further operations on the marker for the load.

#### Markers with a duplicate marker ID

When the game removes the track location listed for one marker, it removes every track location listed under that marker ID, on every segment, so it removes the listed location of every scene marker that holds that marker ID. When the target holds a duplicate marker ID, a modification that would change the target's activity, its `trackMarker` binding or set a `trackFollowing` placement fails before changing anything. The holders are the loaded scene markers with the target's marker ID and every scene marker that owns a registration under it, loaded or not. This applies to Generic markers and crossing pads. The failure is an invalid supplied definition; its diagnosis names the duplicate marker ID and every holder, with each one's activity. The scene patch file's other modifications still apply, including a fixed `placement` of the same marker, unless a map label below the marker caches another track group than its location, which only registering the marker again would correct. A value equal to the marker's current one changes nothing and applies, such as `"active": false` on an inactive marker or the location the marker already has.

A modification that also sets `markerId` on the target to a different ID that is not in use is not refused for this reason. A holder that keeps the shared ID needs `dependsOn` for its own activity, binding or track-following settings, as described in [replacement ID](#replacement-id). The replacement ID applies first, so the target's activity, binding and track-following settings act under its own ID, and the other holders keep their registrations. See [replacement ID](#replacement-id). A `markerId` equal to the marker's current ID does not lift the refusal. A crossing pad takes no replacement ID, so its settings stay refused while another holder keeps its ID. A registration that another holder owns does not count as a registration of the target, so it does not fail the registration rule before changing anything.

### Crossing pad

The patcher recognizes an object holding one `Track.TrackMarker` of type `Crossing` or `Generic` and one `Track.TrackCurveMeshBuilder` with a mesh container and a mesh profile. The target `component` is `Track.TrackMarker`; set `structure` to `crossing-pad-v1`. A pad target named with `track-marker-v1` does not resolve, and a Generic-marker target never resolves to a pad. The track-marker consumer rules apply, except that the pad builder may hold the marker, a pad inside a crossing qualifies, and a pad whose activity follows a track group is refused. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination.

A `trackMarker` reference on a pad moves the marker's stored track location and the track location the game lists for it as for a Generic marker, then runs the game's pad generation. Generation rebuilds the pad mesh along the track at the new location and moves the culling sphere to the new bounds; the game's culling recomputes the mesh visibility on its next update. `{ "detach": true }` fails the modification before changing anything, because a pad without a location has no geometry; disable the pad with `"active": false` to hide it. A placement change also regenerates the pad, so the mesh stays on the track while the object moves.

For a pad with `placement.mode` `trackFollowing`, the patcher also records the track curves the pad was generated from, the segment curves within half the pad width on either side of the marker location, and regenerates the pad when they change, even if its placement does not. The editor checks for changed curves while reconciling track-following placement, including after native Undo and Redo of track edits. It regenerates the pad once the changed curves have held still between two checks. A pad without track-following placement is not regenerated after a track edit by itself; every application of a pad modification, and its removal, regenerates the pad when the recorded source curves differ from the current track.

After a change the patcher verifies the track-marker rules. It also checks that the current mesh was generated from the mod's location and the pad's current position, and that the culling sphere encloses that mesh. A pad generated by the game before the patcher observed it is generated once more on the first application, to the same mesh; later applications leave current geometry alone. A difference fails the modification and restores the captured location, registration, placement and activity, generates the pad again and requires the regenerated mesh to match the captured one; an unverified restoration blocks further operations on the pad for the load.

One native journey on Railroader 0.6.2 with patcher commit `a282015` observed the pad Mg74 shift along the track after a rebinding, refusal of a missing segment and an out-of-range distance, native Undo and Redo, removal, and Save with a native map editor reload that reproduced one modification and one registration. A second journey with commit `ab2816f` measured that the rebinding changed the pad's vertex hash and moved its culling sphere 0.993 metres along the track while the sphere stayed aligned with the new bounds, and that native Undo and Remove modification restored the original vertex hash, bounds and culling sphere exactly. A pad that holds a duplicate marker ID follows the rule under Markers with a duplicate marker ID. See [rebind a crossing pad and regenerate its geometry](rebinding-crossing-pads.md) for editing instructions and limits.

### Milepost

The milepost adapter recognizes an object with one `Helpers.MilePost`, the Generic `Track.TrackMarker` it requires and its `Helpers.TextSynchronizer`. Set the target `component` to `Helpers.MilePost` and `structure` to `milepost-v1`. A milepost target named with `Track.TrackMarker` or `track-marker-v1` does not resolve to the milepost. A Generic-marker target on a milepost is refused before mutation. The track-marker adapter's consumer rules apply, except that the milepost itself may hold its marker and listen to its location changes. The object must have no missing script and no `Track.TrackCurveMeshBuilder`. A modification may set `relationships.trackMarker`, `placement`, `active`, or any combination. Each is a separate setting.

A `trackMarker` setting writes the milepost marker's stored track location and the track location the game lists for it as for a Generic marker, including the track-group subscription when the milepost's activity follows its track group. A detach leaves no listed track location. Placement moves the milepost root and refreshes its renderer cullers without changing the binding. `active: false` is an explicit disable. For a milepost whose activity follows its track group, `active: true` is refused before mutation, as under Markers with group-driven activity.

No setting changes the milepost's model, mile value (`mileage`), prefix, origin flag or displayed text, or recalculates a mile value. The first time something asks a milepost for its origin, the game caches the origin milepost object for that prefix. The cache holds the object, not its location. A placement or binding edit leaves the entry unchanged: it remains absent or present with the same object.

After a change, the adapter verifies the track-marker rules and checks that the prefix, origin flag, mile value, displayed text, model and origin cache entry match their values before the change. It checks rendered texts only if the milepost was active before the change, because text that has never been awake fills in when the milepost first becomes active. A difference fails the modification and restores the captured location, registration, subscription, placement and activity. If the restored milepost state still differs from the captured state, the patcher reports incomplete cleanup and blocks further operations on that milepost for the load.

The capability is incompatible with a game build whose milepost does more than call an empty `UpdateMileage` when its marker's location changes. The patcher does not repair a milepost binding before the game's marker check at world load. A broken binding therefore still raises the game's warning once in that load; the patcher repairs it during normal application. A milepost with a duplicate marker ID follows the rule under Markers with a duplicate marker ID. See [reposition or rebind a milepost](repositioning-mileposts.md) for the editing route and limits.

### Crossing

The patcher recognizes an object holding one `Track.Crossings.GradeCrossingController` whose circuit list is non-empty and whose circuits each hold an approach A, island and approach B span with an ID. The target `component` is `Track.Crossings.GradeCrossingController`; set `structure` to `crossing-coverage-v1`. A modification may set only `relationships.crossingCoverage` and `relationships.crossingEquipment`; `placement`, `active` and `trackMarker` are refused before changing anything.

The patcher records the crossing's circuits and their native spans at its first observation in the load, which requires every native span to be registered in the track graph under its ID. A selected circuit gets three spans owned by the patcher, created disabled under a root that is not saved, given their ID and endpoints and enabled so that the native span lifecycle registers them; a repeated application reuses matching owned spans. The circuit then refers to them. A detached circuit is removed from the controller's circuit list and keeps its native references; the controller evaluates the remaining circuits in their native order from its next pass. A detection addition gets a new circuit set with three owned spans, built the same way, and the controller's list ends with the added sets. The patcher remembers each set it adds by the set's own identity, so an adapter created later over a world that still holds the set reconciles it and does not build a second one. Owned spans that no circuit uses are disabled, which removes their registration, and destroyed.

Before changing anything the patcher requires that the controller evaluates only its native circuits in native order followed by the circuits it added, that each native circuit holds its native spans or owned spans, and that every native span stays registered. After a change it verifies each circuit's coverage, each addition's tracks, that no other added circuit remains, those rules, and that exactly the owned spans in use are registered and cover track. A difference fails the modification and restores the captured coverage, which removes a partly built addition and returns the captured additions; owned spans that stay registered after restoration make cleanup unverified and block further operations on the crossing for the load. The native map editor's Save writes neither the controller's references nor the added circuits nor the owned spans, so each load rebuilds them from the scene patch file. See [correct the detection coverage of an existing crossing](correcting-crossing-coverage.md) and [add detection for extra tracks at an existing crossing](adding-crossing-detection.md) for editing instructions and limits.

For each equipment addition the patcher creates a disabled wrapper with `HideFlags.DontSave` beneath the controller. It records the scope, ID, pattern and source on the wrapper and instantiates the source beneath it at the placement. It checks that the copy's lamps and gates are its own components, that its gates keep their clip and that the controller's key-value object is the copy's nearest one. The patcher then adds the copy to the controller's `Helpers.Culling.GameObjectCuller` list with the culler's current state and enables the wrapper. The copy's lamps and gates initialize through the game's restore notifier and observe the controller's keys. Removal takes the copy out of the culler list, then disables and destroys the wrapper.

When the patcher first observes a controller in a load, it adopts wrappers left beneath it by an earlier application. Repeated world-load callbacks and retained worlds therefore reconcile without duplication. A crossing that no longer stands under its identity releases its additions with its owned spans. A construction failure removes the partial copy and restores the captured equipment; a copy that cannot be released makes cleanup unverified. See [add lights and gates to an existing crossing](adding-crossing-equipment.md) for editing instructions and limits.

For a bell, the patcher creates the same wrapper at the specified placement and records the bell definition as its source. A child object holds an `Audio.IntegerLoopingPlayer` with the crossing bell's mixer group, priority and audio distance. Through the controller's key-value object, the player observes the bell key's current value and every change.

The patcher loads the definition's audio through the content library and keeps the audio reference on the bell. Once loading completes, it configures the player with the clip, indexes and volume from the definition. Removing the bell stops the player, disposes the observer, cancels any pending load, releases the audio reference and destroys the wrapper. If a load completes after removal, it releases its reference immediately. Bells do not join a culler. See [add a bell to an existing crossing](adding-crossing-bells.md) for editing instructions and limits.

### Starter placement marker

The patcher recognizes an object holding one Generic `Track.TrackMarker` held by a car placement of an active new-game setup (`Game.Progression.SetupDescriptor`). The target `component` is `Track.TrackMarker`; set `structure` to `starter-placement-v1`. The marker must have an ID, and the object must not be a milepost or crossing pad. No other component may listen to the marker's location changes, and its activity must not follow a track group. No loaded component other than a colocated `Track.TrackRelativePosition` may hold a direct reference to it. Such a marker never resolves as `track-marker-v1`. A modification may set only `starterPlacement`.

A `starterPlacement` track location writes the marker's serialized location and its registration in the track graph as for a Generic marker: the patcher removes the registration under the marker ID, writes the location, and registers the marker at the new location when the marker is enabled and active. A disabled marker keeps no registration; the game reads its location at a new company game all the same. The object's placement and activity stay as they are. A `wreck` writes the `wreck` field of the one `SetupDescriptor.CarPlacement` that reads the marker; an omitted `track` leaves the marker's location and registration untouched. Before changing anything the patcher requires the track-marker registration rule and refuses a marker whose marker ID another scene marker holds, as it does for track markers. After a change it verifies the location and that rule. A difference fails the modification and restores the captured wreck setting, location and registration; an unverified restoration blocks further operations on the marker for the load.

The game reads the location and the wreck setting in `Game.State.CompanyModeSetup.Setup`, which runs when a new company game becomes active, after the world-load callback in which the patcher applies its scene patch files. The whole-cut check walks the track with the graph's own `LocationByMoving`, reads the segments' availability, and takes each cut's length from `StateManager.DescriptorsForIdentifiers` and `ConsistLength.OfDescriptors`, as the game's `TrainController.HandlePlaceTrain` does before `CanPlaceAt`. See [place a starter placement marker on the track](placing-starter-markers.md) for editing instructions and limits.

## Writing by the native map editor

The native map editor's Save writes the scene patch file as indented UTF-8 JSON with line-feed line endings and no byte-order mark, in the canonical order below. It writes one modification per edited object. The key for a new modification depends on its first edit:

- `bind-<object name>` for a track-marker relationship.
- `cover-<object name>` for crossing coverage.
- `equip-<object name>` for crossing equipment.
- `starter-<object name>` for a starter placement.
- `follow-<object name>` for track following.
- `marker-id-<object name>` for a replacement ID.
- `place-<object name>` otherwise.

Save makes the key unique with a numeric suffix. A prefix that the object name already starts with is written once, so the marker `starter-ewh-int2` gets the key `starter-ewh-int2`. Whitespace and comments of the source are not kept.

Reading a scene patch file never reorders it. Save writes the file only when it has an outstanding change; that write produces the canonical order.

The scene patch file is Saved exactly when its current content equals the text last read or written. For two texts to have equal content, both must parse. At every depth, object members must match regardless of order. A modification's `dependsOn`, when it is an array of strings, matches as a list sorted in ordinal order with duplicates counted. Every other array must match in order. Strings, booleans and null must match exactly, and numbers compare by value.

Numbers are equal when their values, each converted to the nearest IEEE 754 double, are equal. For example, `1`, `1.0` and `10E-1` are equal, as are two literals that differ only beyond double precision. Whitespace and comments never matter. An edit that leaves equal content leaves the scene patch file Saved. The following Save writes nothing and leaves the file's bytes unchanged. If no scene patch file existed at load, it is Saved while it has no modification.

Save refuses to overwrite a file that changed outside the editing session. Its current content must equal the text last read or written, using the same equality described above. Reordering or changing spacing outside the editing session does not count as a change and does not block Save. A changed value, an unparseable file, a file deleted since it was read, or a file created after a load that found none requires a version choice. A file that needs repair requires a verified backup before the editor version can replace it. If the file was deleted, **Keep editor version** needs no backup and the next Save writes the editor version.

Unknown fields at the scene patch file root and unknown modifications are preserved without being executed. Every number the native map editor did not replace keeps its source text. For example, `0.10` or a number beyond double precision is written back unchanged, even inside a modification changed in the editor. The engine rejects unknown fields inside a modification, target, placement or relationship when it tries to execute that modification. The native map editor preserves such a modification without editing or removing it.

The native map editor protects and does not write a scene patch file in any of these cases:

- Its format version is missing, malformed or unsupported.
- `modifications` is an array or any other value that is not an object.
- A modification key is empty or a modification is not an object.
- A property name repeats in any object.
- The file is for another map.
- Its numbers cannot be matched to their source text.

Format version 1 has no earlier released shape; the array form of `modifications` is not converted. The editor shows where a protected scene patch file is wrong and offers to read it again after an outside correction or to start fresh behind a backup; see [Recover a protected scene patch file](recover-scene-patch-files.md).

### Detecting an outside file change

The patcher compares the file on disk with the scene patch file it last read or wrote. Whitespace, object-member order and the order of entries with stable identities do not affect equality, so reformatting alone does not trigger a version choice. When content differs, the editor offers **Use file version**, **Keep editor version** and **Import**. Until you choose one, Save writes nothing and shows the reason. Import compares the file with the text last read or written and the session's scene patch file with its state at the start of the session, so a Save during the session creates no conflict by itself. Import checks the declared `formatVersion` before any other content, and keeps unknown members of the file and of its modifications without executing them; a later Save writes them unchanged. The comparison runs when the game regains focus and when you click **Refresh** or **Save**. See [Edit and save fixed placement in the native map editor](editing-placement-in-the-map-editor.md#choose-a-scene-patch-file-version).

### Construction segments

`constructionSegments` sets the construction segments of a rail bridge construction: the track segments whose construction state its construction controller (`Game.Mow.TrackConstructionScenery`) reads to show or hide the bridge's parts. It requires patcher 0.5.0 or later. Two targets hold it:

- The group root of a rail bridge construction that carries its controller on the root: `World/Large Scenery/Bryson/Bridge 1` and `World/Large Scenery/Almond Bridge`, with `UnityEngine.Transform` as `component` and `baked-scenery-group-v1` as `structure`.
- A construction controller on an object of its own, `World/Large Scenery/Ela/Ela Bridge Scenery`, with `Game.Mow.TrackConstructionScenery` as `component` and `bridge-construction-scenery-v1` as `structure`. Its construction segments are its only setting: a `placement` or `active` that differs from the object's own fails before changing anything, because the controller's position has no effect in the game and the bridge's construction visibility runs on it.

| Setting | Meaning |
| --- | --- |
| Omitted | The construction keeps its native segments. |
| `{ "segments": [<segment ID>, ...] }` | Replaces the construction segments with exactly these segments. |
| `{ "detach": true }` | Fails before changing anything: a construction without segments has no meaning. |

`segments` lists at least one segment ID, each once, in any order; the native map editor's Save writes them in ordinal order. Before changing anything, the modification fails if a segment is missing from the loaded track or more than one loaded segment carries its ID, and the message names that segment; no other segment is used. The construction's native segments are accepted as they are, also when one of them no longer exists. The modification also fails on any other target, including a plate girder bridge and a part of a construction.

The patcher writes the segment IDs to the controller and drops the controller's cached segments. When every segment exists and the controller is enabled, it updates construction visibility. Otherwise the parts keep their visibility until the world reloads. The patcher verifies the controller's segment IDs after a write. Applying the same segments again writes nothing. A failure after the write restores the controller's pre-edit segments. An unverified restoration blocks further operations on the object until **Recheck** verifies the state or the world reloads. Removing the modification returns the native segments. At world load, the patcher defers the controller's update when a native segment is missing and the last acceptable setting in load order supplies a complete replacement set on the loaded track. A later setting that restores the native set takes precedence and leaves the native update in place. The game runs a deferred update one frame after the patcher applies the scene patch files. In Railroader 0.6.3, Bryson Bridge 1 loaded without the stale-segment error after repair and showed or hid parts as the new segment's construction state changed. See [repair the construction segments of a rail bridge](repairing-construction-segments.md) for the cases where the native error still occurs.

### Canonical order

A Save that writes the scene patch file writes its content in this order:

- Scene patch file root: `formatVersion`, `sourceMap`, `modifications`, then unknown members.
- `sourceMap`: `mapId`, then unknown members.
- The keys of `modifications`, `circuits`, `additions` and `crossingEquipment` are in ordinal order.
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

The supplying mod's `Info.json` must list `crankyant/scene-patcher` under `dependencies.required`; the runtime skips the scene patch file of a mod that does not. The native map editor's Save adds or raises that entry.

| Scene patch file content | Minimum patcher version |
| --- | --- |
| Fixed placement only | `0.1.0` |
| Any modification with `active` | `0.2.0` |
| An independent-board target other than the legacy Sylva location sign | `0.5.0` |
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
| Any modification whose target `structure` is `baked-scenery-group-v1` | `0.5.0` |
| Any modification whose target has `padMarkerId` | `0.5.0` |
| A modification whose target `structure` is `baked-scenery-part-v1` | `0.5.0` |
| A modification whose target `structure` is `spawn-point-v1`, or whose target has `Character.SpawnPoint` as its component and no `structure` | `0.5.0` |
| A modification with `UI.Map.MapIcon` as its target component or `station-map-icon-v1` as its target `structure` | `0.5.0` |

Fixed placement is the base minimum. Other scene patch file content can raise it according to the table, whether or not the target is loaded or its capability is available on this build. The legacy Sylva location sign does not raise the pattern-based board dependency minimum.

Save writes `crankyant/scene-patcher >=<minimum>` without an upper bound. It keeps the entry's other constraints and does not lower a minimum. If a constraint already requires that version or a later one, Save leaves the entry unchanged. If an existing constraint excludes the required minimum, Save reports the conflict and does not write the entry. Runtime reporting adds no scene patch file, schema or capability requirement.

A target whose `structure` names a capability that is incompatible with the running game build fails with that reason; other modifications still apply. A failure during target preflight or during a change is recorded for the current load and is not retried during that load. If cleanup after a change cannot be verified, later modifications that resolve to that target fail closed.

## Failure isolation

The runtime handles failures according to when they occur and whether cleanup succeeds:

- The runtime skips a whole scene patch file if it cannot read the file, its `formatVersion` is unsupported, it has a repeated property name, its `modifications` value is not an object keyed by modification ID, or its scene target map is outside the loaded map context. None of the file's modifications runs.
- A modification fails before changing anything if it has an invalid value, its target resolves to no object or several objects, or a prerequisite fails. The object keeps its state. Every modification that lists the failed modification in `dependsOn`, directly or through another modification, fails as a dependency failure without changing anything. Modifications that do not depend on it still apply.
- If a modification fails after a change starts, the patcher restores the state captured before changing anything, including relationships and detection additions, and checks the restoration.
- If that check fails, the report names the remaining or uncertain state, such as a position that differs from before or owned spans that remain registered. Later modifications of the same object fail closed until a later world load rebuilds the object.

Work applied before a failure stays in place, and gameplay continues. The runtime does not retry a failed modification while that world stays loaded. The next world load reads and evaluates every scene patch file again. In the native map editor, **Recheck** compares the object with the scene patch file and ends the guard once they agree; see [retry after a failed edit](editing-placement-in-the-map-editor.md#retry-after-a-failed-edit). Failure handling adds no JSON fields or format version.

## Application on each peer

Format version 1 has no multiplayer fields. The host and every client read `Data/scene-patches.json` from their own installed copy of the mod and apply it to their own scene. Each peer checks `sourceMap.mapId`, target identity and capability prerequisites. `placement.position` is in map coordinates and is converted through each peer's own world origin.

A repeated world-load callback reconciles the stored placement without accumulating offsets. It does not retry failed modifications. A later load of the same map, including reconnect and same-map re-entry, starts a new load attempt. A target that still resolves under its identity keeps its applied state and any unverified-cleanup guard. A target that the game rebuilt gets the modification as new. For the old object, the patcher removes only what it owned, such as the spans of a detection addition.

Failed modifications are retried once per load attempt. A cleanup failure is reported on that peer only and retried at the next load. Modification keys and addition IDs are the persistent identities across load attempts. In single-player, loading the same save again in one process rebuilt every modded object and added object, and each modification was applied once. This behavior hasn't been checked in a multiplayer session, for any kind of target.

Crossing changes follow the same rule on every peer. In the code of Railroader 0.6.3, a crossing controller computes its detection in each player's own game and sets its light, gate and bell keys from it. Whether those keys also reach other players hasn't been determined. A player who leaves, loses the connection or joins another session returns to the main menu, which unloads the map, so a later join or reconnect is a new world load on that peer. The patcher builds that mod's detection additions, lights, gates and bells once in the new world, under the `crossingCoverage` addition IDs and `crossingEquipment` IDs of the scene patch file, and applies the circuits and the modifications of existing masts and bells once. What it built in the earlier session went with the unloaded map, including each added bell's audio. A crossing modification that fails on one peer is reported on that peer only. The other modifications of that peer and the other peers keep their state. This behavior hasn't been checked in a multiplayer session.

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
        "structure": "independent-board-v1"
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

A completed [transform gizmo gesture](using-transform-gizmos.md) writes the same fixed placement as numeric editing. Position remains in map coordinates and rotation uses quaternion order. Preview frames are temporary and do not appear in the scene patch file. The gesture creates no logical binding. On an object that follows a track, the gesture's result is stored as a new `offset` and `rotation` relative to the same track. Native Save writes the edited mod's canonical scene patch file and dependency metadata; Undo and Redo reverse completed gestures through native history.
