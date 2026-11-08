# Additional crossing detection research

Issue [21](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/21) supplies evidence for [19](https://github.com/CrankyAnt/RailroaderScenePatcher/issues/19#issuecomment-6716850783). This draft covers the detection component path. Runtime construction, train passages, physical equipment, removal and fresh-world reproduction remain untested in this draft.

## Sources and limits

The installed `Railroader_Data/Managed/Railroader.dll` SHA-256 is `8E9ACFD583C1DA79655C970773D42F2179E999F62DC36A3C6BC3A7B94C27A91E`. The static observations below come from method bodies and metadata in that installed assembly. The independently authored `Metadata/Program.cs` uses .NET `System.Reflection.Metadata` to inspect it; it does not include the game's code. The assembly owner is the game, not this repository. Findings apply to the inspected build only. The static inspection does not demonstrate runtime behavior.

The prior [crossing probe](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/b41a6dda255accf2de1949414b35ea37f4b8d63c/Research/CrossingCircuitProbe/CrossingCircuitProbe.cs) and [operation report](https://github.com/CrankyAnt/RailroaderScenePatcher/blob/b41a6dda255accf2de1949414b35ea37f4b8d63c/Research/ApplicableSceneOperations.md) establish a useful existing control and telemetry approach. Their endpoint mutation is not evidence of new component creation. The report's recorded crossing has two sets and six distinct original spans. Physical lamp flashes and audible bell were not independently verified in that experiment.

## Static findings

- `Track.TrackSpan.OnEnable` calls `Graph.AddSpan` when the shared graph exists and the span ID is nonempty. `OnDisable` calls `Graph.RemoveSpan` under the same conditions.
- `Graph.AddSpan` uses a dictionary `TryAdd`. `RemoveSpan` removes by ID, without checking the object identity. An ID collision is therefore hazardous even during cleanup. The trial requires unused IDs and checks that lookups point to its own objects before disabling them.
- `TrackSpan.lower` and `upper` serialize resolved locations and invalidate the cache when set. `IsValid` checks both resolved locations. `GetPoints` and `GetSegments` rebuild the cache on demand through `Graph.FindPoints`. Valid endpoints alone do not establish useful connected coverage.
- `GradeCrossingController.UpdateCircuit` reads the current circuit-set list, checks `AnyCarsOnSpan` for the island, and estimates approach ETA for both approaches. Its nested `CircuitSet` has `approachA`, `island` and `approachB` fields. This static path supports testing an appended set; it does not establish activation, timing, physical devices or a general topology requirement.
- `SerializableLocation` holds a segment ID, distance and end. The trial requires these explicitly for each endpoint; it does not infer crossing membership from proximity.

## Trial artifact and prerequisites

`DetectionTrial.cs` is independently authored research code. Its net472 build against the installed Unity core assembly succeeded with zero warnings and errors. It has not run in the game. The calling host must guard the exact disposable map and save, arm file, controller path and world state. It must verify physical train clearance before mutation and cleanup, because occupancy of invalid or incomplete spans cannot prove clearance. The host must call `Remove` on exit and abandon the disposable session without saving if restoration fails.

The host supplies three separately identified spans for this particular experiment: approach A, island and approach B. Each input supplies a unique ID and two explicit segment/end/distance locations. The trial creates them inactive, checks resolved endpoints and nonempty computed coverage, enables and checks exact graph registration, then appends one set. It samples original and new span occupancy and controller light, gate and bell signals. Cleanup checks clearance and ownership, removes its membership, disables its objects, checks registrations are gone, and compares original membership, references, endpoints and registrations with the captured baseline. This does not by itself verify physical gates, lamps or bell, nor detect every dependent state change.

The code assumes the selected controller's original two-set topology and the concrete `IList` representation seen in this build. Its one new three-span set is a trial choice, not a requirement for every crossing. The test needs the explicit parallel segment and range endpoints, a confirmed crossing of the road, a driveable train, both clear routes, and a host that records visual/audio observations separately. Reproduction in a fresh world and reload timing remain open until actually tested.
