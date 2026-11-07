# Preliminary scope inventory

Research checked 2026-11-07 against the installed experimental game, official documentation, read-only Discord searches and accessible Nexus listings. This is a discovery inventory, not a complete compatibility claim. No mods were installed or game/save files changed in this research round.

## Findings

| Family | Evidence | Proposed treatment |
| --- | --- | --- |
| Legacy scene TrackMarker locations | Three Sox Ela failures already reproduced and repaired locally. Installed code distinguishes Generic, Signal, Flare, Crossing and PassengerStop marker types. Physical object names do not establish enum values. | Confirmed first scope: explicit marker identification and guarded rebind. Disabling a marker or hiding its owning object remains untested. |
| Scene TrackSpan endpoints under CTC blocks | TrackSpan stores serialized lower/upper locations. CTCBlock reads child TrackSpan components. Missing/invalid endpoints have diagnostics. No current rework failure reproduced. | Strongest additional candidate. Require an affected object and test before promising support. |
| Industry and passenger spans created from OpsDTO | Native JSON supplies endpoints and the loader creates runtime components. | Repair in native map data. Do not use their runtime component type alone to justify DLL scope. |
| Native built-in object anchors | Sox water column fixed in map JSON through the editor. | Native authoring support; outside runtime repair core. |
| CTC signal-to-block relationships | CTCAutoSignal can report a null block. This is a scene object reference rather than a track endpoint. | Separate candidate. No current rework cause demonstrated. |
| Milepost origin relationships | Milepost position uses TrackMarker; origin chains are separate references. | Position may be covered by marker support. Missing-origin repair has no demonstrated rework use case. |
| Progression, construction groups and retained scenery | Editor limitations are documented, but no concrete additional location/span failure was established. | Keep on discovery list. Do not promise generic scene mutation. |

## Discord symptom searches

The current round supplemented earlier targeted marker searches with `"is not on the track"`, `"missing point"`, `span invalid`, `"null block"`, `segment missing` and `unresolved`. Search matching is broad and results may omit image-only logs. These are not exhaustive server searches.

- `span invalid` returned five messages, all predating the 2026-10-27 experimental release. They include a FUSE Ela passenger-stop binding warning and old Strange Customs invalid span endpoint messages. These demonstrate historical reference families, not current 0.6 defects.
- `unresolved` returned six messages, none from the 0.6 period. No relevant current scene-reference failure was found in these results.
- `"null block"` returned no results. This is a limited search outcome, not evidence that the failure cannot occur.
- Earlier current-period evidence about noneditable crossing/trigger markers remains recorded in DiscordMarkerResearch.md.

No posts, messages or reactions were sent. Do not turn an author's speculation about AI pathfinding into a demonstrated marker fault.

## Test corpus

Start with unmodified Sox Ela as the known positive control and the available Katers Sylva archive. East Whittier MoW Yard (#2040) is the highest-priority external lead, pending an accessible current archive and compatibility verification. Whittier Crossover (#1947) and Bryson City Industries (#2003) explicitly target 0.6.x but have no established failures. They are useful controls, not presumed broken mods.

Nexus posts often returned HTTP 403. This limits report verification; no bypass was attempted. See NexusReports.md and SylvaZipAudit.md for package and source details.

Test each compatible pack separately against a fresh baseline. Record game build, pack version, enabled pack IDs, editor/save route and new log section. Ensure the existing Ela repair DLL cannot mask the positive control. Compare a clean base map, the editor load and a fresh sandbox save. Do not initially combine reworks or use the user's normal save. Before changing global mod configuration, record it so it can be restored.

The next naming decision should distinguish a marker-only implementation from broader track-reference repair. No repository or implementation was created in this round.
