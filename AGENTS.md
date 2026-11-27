# RailroaderScenePatcher

## Agent skills

### Issue tracker

Issues, specs, Work Briefs, maps and tickets live in GitHub Issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Ordinary requests use the Type, Triage, Ready, Status and Resolution vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

This repository uses a single-context layout. See `docs/agents/domain.md`.

### Other configuration

Research, delivery paths including direct-main, publication rules and CI: see `docs/agents/`.

## Repository standard

Documentation goes to one of three places:

- `docs/guides/`: for the people who use the product.
- `docs/development/`: for whoever develops or extends the product, agents included: how a part works, how a developer uses it, how to extend it.
- `docs/sources/`: for the same readers, material from outside the repository, kept as `docs/agents/research.md` says.

The product's own technical reference, such as its APIs, commands, configuration or data model, lives with its audience in `docs/guides/` or `docs/development/`.

Each document in `docs/guides/` and `docs/development/` is one [Diataxis](https://diataxis.fr/compass/) type: tutorial, how-to guide, reference or explanation. Why a decision holds is an ADR; a term goes to `GLOSSARY.md`.

The top level of `docs/` holds only `adr/`, `agents/`, `guides/`, `development/`, `sources/`, `out-of-scope/` and the exceptions below. Inside `guides/` and `development/`, a folder holds one broad subject, such as `docs/development/skills/` or `docs/development/modules/`, never a single document. It comes with its first document, when that document clearly passes the Diataxis test and has no standard place yet.

Temporary documents, such as research, notes and plans, stay out of `docs/`.

A place outside this standard, such as another top-level folder in `docs/`, is an exception. It enters `### Exceptions` as one line with its path and its reason, only after the user approves it; a change that ends the exception removes its line.

A rule for the whole repository goes into `AGENTS.md` under the [agents.md](https://agents.md/) heading that fits its subject, such as `## Code style`, `## Security considerations` or `## PR instructions`, or under a heading of its own when none fits. A rule on the subject of a file in `docs/agents/`, such as a delivery rule or a CI choice, goes into that file.

When in doubt about a place, ask the user.

### Exceptions

## Project overview

Every tracked top-level folder whose name does not start with a dot gets one line: the path and at most one short clause that names the folder's purpose, not its contents. No explanation, rules, status, subfolders, nested lists or file pointers. A new top-level folder adds its line; a removed one removes its line; a change that makes a clause untrue rewrites it in the same change.

- `LICENSES/`: canonical license texts
- `Src/`: production mod and document engine
- `Tests/`: automated behavioral validation
- `Tools/`: development helpers for game journeys
- `docs/`: maintained repository documentation

## Testing instructions

One line per validation or test command and per CI workflow the repository runs. A change that adds, removes or renames a command or workflow, or changes what it validates or tests, adds, removes or rewrites its line in the same change.

- `reuse --no-multiprocessing lint`: validates file-level copyright and license information
- `git diff --check`: checks changes for whitespace errors
- `actionlint`: validates GitHub Actions workflow definitions
- `dotnet build Src/RailroaderScenePatcher -p:GameManaged=<Railroader_Data/Managed>`: builds the DLL mod against the installed game assemblies
- `dotnet run --project Tests/RailroaderScenePatcher.Core.Tests --configuration Release`: validates the core library's behaviour without game assemblies, the category, kind and place rules and the outliner's tree against the listed objects of the base-game map fixture (the fixture's expected outcomes are a reviewed snapshot of the rules' own output; top scene objects, known category changes, bridge and sign kinds and town places are checked independently), the place rule with synthetic places (the source order of place positions, the 500-metre boundary of a moved object, the choice by path within 1500 and 3000 metres, and a moved object listed under its new place right after an edit while moved sources keep their original positions), that every base-game map fixture place the position order changes is named and justified, that each family value and family label of the base-game map fixture changes only by the family mapping, that a whole scene patch file whose modifications carry a retired structure id fails each of them with the no-match row, applies the others and stays unchanged, the Supported edits of every base-game map fixture object and fictional object against an explicit expected list of edits and reasons, their disabled tooltips and the 45-character reason limit, the outliner's reason on a map where the game creates every checked scene object while it runs, its log output in player and debug mode, that every patcher log write goes through the logging entry point, that no text object inside a native control is switched off, that no issue-named compile constant remains, that the window calls a complex "group" and a child object "part" with no window text saying "complex" or "child object" and no window or Log text naming the retired scenery terms or saying "object list", that the help names the empty-map state and problems with the window's own terms and the select instruction has one wording, that every shown number goes through the shared number formatter, that the code has no Pavilion kind or Track-relative family, that every pattern refusal maps to a short reason, that gizmo failures write one player error per drag, that a gizmo drag follows the object's own axes, that starter markers no active setup uses stay unlisted, that a target for an object that shares its hierarchy path and component resolves exactly by its marker ID or map position while unkeyed targets keep their outcomes, the keys and sequence numbers of the 78 base-game map fixture objects that share a path (a reviewed snapshot; the Alarka branch, Andrews and Sylva numbers are checked independently), that a mod object's modification holds its source, its own target and its placement and is built at each load, fails alone without its source, leaves the world with its modification, takes the edits of its family, is duplicated from its own source, is removed with its later edits and listed where it stands (fictional worlds with the station building and a station map icon of the base-game map fixture), the window states of Duplicate with recorded pointer input, where only a left click on the world places, a ghost follows the pointer while placing and the click places at the ghost, Release and Esc return to the blank window, and an outliner selection asks before it cancels the duplicate, and the reasons that keep Duplicate inactive, and that the normal build has no fault injection switch; append `-- --suite <test-class>` to run one test class
- `dotnet run --project Tests/RailroaderScenePatcher.Core.Tests --configuration Release -p:SceneFaults=true -p:BuildArtifactsRoot=<fault build root>`: validates the test-only fault injection points in a fault build of the core library
- `dotnet build Tests/FaultInjection --configuration Release -p:GameManaged=<Railroader_Data/Managed>`: builds the test-only switch that arms a failure in a fault build during a native journey
- `dotnet build Tools/TerrainHeightReadout --configuration Release -p:GameManaged=<Railroader_Data/Managed>`: builds the terrain height readout that a native journey uses to compare the terrain height at fixed places
- `dotnet build Tools/NativeObservation -p:GameManaged=<Railroader_Data/Managed>`: builds the isolated native observation artifact
- `pwsh -NoProfile -File Tests/FixedPlacementJourney/Test-FixturePreparation.ps1`: validates baseline-derived fixture generation in the keyed document shape with its key orders, and the common dependency minimum, with synthetic measurements
- `pwsh -NoProfile -File Tests/FixedPlacementJourney/Test-TestHome.ps1`: validates test-copy ownership and cleanup, and the debug option of the test copy, in a synthetic home
- `pwsh -NoProfile -File Tests/FixedPlacementJourney/Test-AttemptSummary.ps1`: validates separation of active-world and unassigned attempt evidence read from the patcher's debug lines, including a line that ends with the game's property suffix
- `ci` (shared, `v1`): runs repository hygiene and the configured CI parts and reports the one required check `ci / verdict`
- `automation` (shared, `v1`): runs the configured parts that write, including Dependabot auto-merge, additions, full recording and issue-title normalisation
- `release` (shared, `v1`): runs the configured release part on a pushed tag or a start by hand and publishes nothing without release configuration

## Code style

Keep `README.md` user-facing. Describe the product, its audience and its availability; keep development plans, implementation decisions and unresolved development choices at their appropriate owners. Add usage instructions only when they describe established behavior.

## Writing style

User-facing text, in the product and in `docs/guides/`, follows the [Google developer documentation style guide](https://developers.google.com/style). Where it does not cover a case or its term does not fit, follow the [Microsoft Writing Style Guide](https://learn.microsoft.com/en-us/style-guide/welcome/); these two are the only style guides. Buttons are clicked; rows, items and objects are selected.
