<!-- SPDX-FileCopyrightText: 2026 CrankyAnt -->
<!-- SPDX-License-Identifier: MIT -->

# Crossing detection probe

This research probe targets only `crankyant/crossing-detection-research` and the Bryson Everett crossing. Use an isolated disposable save. Do not save the session. It makes no train movement. Physical equipment behavior requires separate observation. [The feasibility report](../AdditionalCrossingDetection.md) records the completed bounded runtime trials.

Build against the installed managed assemblies with `dotnet build CrossingDetectionProbe.csproj -p:GameManaged=<managed-directory>` and set intermediate and output paths outside the checkout. Install only for the isolated research session. The host uses its mod directory for `CrossingDetectionProbe.log`, `command.txt`, `spans.json`, `ARM_CROSSING_DETECTION` and `ASSERT_PHYSICAL_CLEARANCE`.

The GUI buttons and a one-command `command.txt` accept `inspect`, `input`, `focus`, `overview`, `pause`, `capture`, `add`, `sample` and `remove`. The host deletes a command file before executing it and logs the result. `input` compares the original Unity JSON parser output with a strict parser and logs the span count, IDs and endpoints without mutation. The strict parser requires every expected field, rejects unknown and duplicate fields, and uses the Newtonsoft.Json assembly provided by the game. `capture` starts baseline telemetry every 0.5 seconds. Add requires that baseline and continues telemetry for the added circuit; removal and world unload stop it. Telemetry stops on identity or sampling errors. `pause` requires both the probe directory and the process `-homedir` argument to match the isolated RuntimeHome. `overview` requires `focus` first. Inspect reports original spans, nearby span points and candidate train identities. Inspect logs the direct `GetPoints` output and an explicitly named `WorldToGame` conversion without treating the latter as a coordinate correction. It may not enumerate unspanned graph segments. Select and verify endpoints before addition.

Add and remove require both empty guard files. `ASSERT_PHYSICAL_CLEARANCE` is an operator assertion that the original and proposed routes are physically clear, including areas outside the spans. Refresh that assertion before each mutation. The probe also checks span occupancy and refuses cleanup when identity or occupancy checks fail. If cleanup fails, abandon the disposable session without saving. Leaving the world attempts guarded cleanup; do not rely on it as a substitute for explicit removal.

Supply `spans.json` with exactly three entries in approach A, island, approach B order. Each unique ID must start with `CrankyAntDetection21_`. Replace the example segment IDs and distances only after inspecting the graph. The endpoint end is `A` or `B`; distances use the game's native units.

```json
{"Spans":[
  {"Id":"CrankyAntDetection21_A","Lower":{"Segment":"example","End":"A","Distance":0},"Upper":{"Segment":"example","End":"A","Distance":10}},
  {"Id":"CrankyAntDetection21_I","Lower":{"Segment":"example","End":"A","Distance":10},"Upper":{"Segment":"example","End":"A","Distance":20}},
  {"Id":"CrankyAntDetection21_B","Lower":{"Segment":"example","End":"A","Distance":20},"Upper":{"Segment":"example","End":"A","Distance":30}}
]}
```

The probe relies on internal APIs of the inspected game build. A successful build does not establish that reflection, component registration, graph coverage or physical crossing equipment works at runtime.



`capture` also starts the read-only gate observer for the two existing signal devices. It records the resolved component paths and instance IDs, an initial snapshot, and subsequent controller output changes. In `LateUpdate` it compares each arm's local rotation and position with the preceding frame. It logs motion starts, stops and rotation direction changes, plus snapshots at least every 0.1 seconds while movement continues. A rotation difference over 0.01 degrees or position difference over 0.00001 units marks a frame as moving for logging only; neither threshold defines an open or closed gate. Each record contains the frame number, monotonic stopwatch ticks and elapsed seconds, local rotation and position, controller outputs, component activity and available animator/playable state. The existing circuit occupancy samples remain on their 0.5-second schedule. Observer initialization or identity failures appear in the log and stop gate observation; they do not establish a physical result. `remove` and world unload stop the observer.

Gate records also retain the previous frame's and current frame's `gates` key presence, value type, null flag and boolean value, alongside each consumer's invert setting, configured speed, clip duration and playable time, speed and play state. An unavailable getter is marked in the log. These are observations of the existing components; they do not intercept callbacks or modify equipment.
