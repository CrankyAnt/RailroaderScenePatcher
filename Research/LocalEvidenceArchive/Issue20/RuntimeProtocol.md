# Candidate ownership capture

Prepared for [Recover ownership of unresolved inventory candidates](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/20). This protocol was prepared for an isolated capture. Preparation is not runtime evidence.

## Question

Identify the concrete owners, hierarchy, component composition and direct consumers of `Mkwd` and `Mi4f`. Their historical warning locations were `Sjdl` near 38.6 m and `S9sc` near 225.7 m. These rounded distances are corroboration, not exact selectors. Do not damage the graph to reproduce the warnings.

## Environment and preparation

Use an isolated test environment and protect the normal profile throughout.

1. Record the installed game build and `Railroader.dll` SHA-256, probe DLL/source hashes, isolated home, intended map and resolved layers. The earlier assembly hash was `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`.
2. Before installation, record current pack manifests, enabled state and hashes. Install only the authored candidate probe and manifest while the game is stopped. Do not replace any existing probe.
3. Use the existing clean `OwnershipResearchA` map or another verified inherited-base control. Exclude the intentionally damaged inventory and `EditorFixture14`. Existing folders alone do not establish the selected map, enabled packs or baseline.
4. Start a fresh process with the isolated `-homedir` and a unique `Player-Issue20-<run>.log`. Verify the actual command line and selected fixture. Do not save a normal profile or copy a personal save.

## Capture

Run the probe's explicit read-only capture after the map and graph are available. Preserve its raw output locally. For each marker require:

- The number of ID matches, including inactive loaded objects. Zero or multiple matches is a finding, not permission to choose the closest object.
- Scene, full hierarchy path, root, parent chain, instance IDs as run-local evidence, activeSelf, activeInHierarchy and component enabled state.
- Actual marker enum, exact serialized segment/end/distance, graph resolution and registration details where available.
- Attached components, ancestors and children, with renderer, collider, text, culling and track-relative relationships distinguished.
- Each track-relative component's target and group-following settings, if present. Name the actual target rather than assuming it is the marker root.
- Bounded direct component-field object references to the marker, target and relevant parent/child objects. State the inspected assemblies, scenes, field visibility and collection depth. No direct match does not prove absence of indirect, scripted, cached or identifier-based consumers.
- Any demonstrated native map-object/DTO ownership association. A runtime TrackMarker alone does not decide retained-scene versus native ownership.

Capture the log interval, warnings and any probe refusal. If visual identity matters, focus each uniquely resolved object using the established helper, then record a screenshot and matching path. Camera focus is not native editor selection. Do not move, rebind, disable, call placement methods, change groups or test train behavior.

## Return contract

Return the frozen probe log, selected sanitized Player lines, before-state hashes, game/map/layer/probe identities and capture boundaries. Include screenshots only when they establish a named identity. Record whether the capture changed any files or state unexpectedly. Quit without saving; restore the added probe installation and enabled-pack changes, then verify the prior hashes and configuration. Preserve existing evidence and other active investigations.

Full Player logs remain local because they can contain account identifiers. Publish only reviewed observations and authored tooling. A second capture is needed only if the first is ambiguous or incomplete; mutation and restoration behavior belongs to the separate operation investigation.

## Passenger-source input

The historical FUSE Ela warning requires its original message or log and exact package/version or fixture. A current runtime scan cannot reconstruct that historical owner from the paraphrase. If the source becomes available, retain its source pointer locally, extract only the necessary warning identity, and trace that exact instance to operations data or retained scene ownership. A synthetic passenger fixture proves its own route only. Do not label the historical warning a current defect or exclude passenger objects because the source is missing.
