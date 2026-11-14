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
- `docs/`: maintained repository documentation

## Testing instructions

One line per validation or test command and per CI workflow the repository runs. A change that adds, removes or renames a command or workflow, or changes what it validates or tests, adds, removes or rewrites its line in the same change.

- `reuse --no-multiprocessing lint`: validates file-level copyright and license information
- `git diff --check`: checks changes for whitespace errors
- `actionlint`: validates GitHub Actions workflow definitions
- `dotnet build Src/RailroaderScenePatcher -p:GameManaged=<Railroader_Data/Managed>`: builds the native mod against the installed game assemblies
- `dotnet run --project Tests/RailroaderScenePatcher.Core.Tests --configuration Release`: validates complete-document application and protection, per-peer local-world binding with retained-world re-entry and world replacement, retained-world reconciliation across load attempts with retained guards and underlying states, retried failures, released reconstructed targets, owned-span cleanup, isolated cleanup and reconciliation failures and single observers across load attempts, multiple-MapMod discovery, load order and informational overlap notices with failure isolation, capability isolation and composition, bridge placement with collider and separate-scenery verification, independent-board culling, mask registration and terrain output with their failures, track-marker relationship set, replacement, detachment and omission with graph registration, crossing-pad rebinding with generated geometry and culling regeneration, its restoration and guarded retry, pad inventory freshness under culling toggles and with a saved mesh under the pad's container, following-pad regeneration on longitudinal and lateral curve changes and their undo, also without a curve token, once per settled drag, removal regenerating from the restored track, with reconciliation checks that resolve, scan and build nothing while unchanged and pose-driven follow updates without resolution, consumer scan or warning search, crossing-coverage selection, detachment, retention and preview with owned-span registration and restoration, detection additions with document-scoped identity, explicit tracks and area, reuse on repeated application and re-entry, deletion, isolated dependent failure and restoration, unresolved-target refusal and restoration, single-pass reference scanning in one call or per owner and once-per-discovery setup indexing, inventory refresh and presentation coherence, outliner search with the All, Problems and Changed filters, family grouping and auto-fit width, changed-object lookup without a scene search, stepped stale checking with its cancellation, failures, stale reasons and components leaving during a pass, runtime state, runtime references and culler-driven activity of crossing equipment that do not mark stale, two scene scans for an edit pinned for immediate use and one after a stepped pass, a guard that observes only the target and the re-observation of one entry, origin rounding that does not mark stale, an object outside the moving origin that an origin move does not mark stale while its own move does, the related observations an own edit changes, adds or removes, a compared snapshot that pins without a scan, messages newest first with one follow-up kept beside the action result a repeated result listed once and a withdrawn announcement and a stale reason that names the difference, track-following reconciliation without a scene search each time, stale-target revalidation, edit feedback for every refused or blocked edit, status-lamp words and colours, the fit of the lamp words, a saved window size restored as saved, supplied definitions that stay current through the patcher's own Save and turn stale on an outside change of the document until a refresh, band commands disabled only for their own reason, row markers before the name, the native state badges and the default window placement below the Map Editor or at the top right, one scene search per inspection for the panel's terrain output, world selection, confirmation that finds one identity without a discovery and a refusal that names what differed or the shared identity, clipped world ribbons and shared picking, explicit track and endpoint snapping with fixed-document persistence and restoration, track-following placement with track-move reconciliation, repeated application, refusal, restoration and persistence, group-subscription coherence with explicit-disable precedence, an explicit disable kept by a modification without an activity value in another MapMod's document or the same document and by a gizmo preview, a following object that follows a track turn of a fraction of a degree, native group activity and restoration, gizmo preview and cancellation with dependent-state restoration, gizmo rotations of a fraction of a degree completed as one operation with a restoration that verifies within its read-back tolerance, a drag preview that observes only its target, fixed gesture persistence and origin-aware history, runtime diagnostic attribution with early repair, exact warning reconciliation and native serialized-reference restoration through Remove, Undo and Redo, authoring, history accounting and persistence with write failures through controlled scene, board, bridge, track-graph, history and storage dependencies
- `dotnet run --project Tests/RailroaderScenePatcher.Core.Tests --configuration Release -p:DefineConstants=ISSUE39_OBSERVATION -p:BuildArtifactsRoot=<absolute-observation-output>`: validates attempt tracing and the core behavioral suite in a separate instrumented build
- `dotnet build Tests/NativeObservation -p:GameManaged=<Railroader_Data/Managed>`: builds the isolated native observation artifact
- `pwsh -NoProfile -File Tests/FixedPlacementJourney/Test-FixturePreparation.ps1`: validates baseline-derived fixture generation and the common dependency minimum with synthetic measurements
- `pwsh -NoProfile -File Tests/FixedPlacementJourney/Test-TestHome.ps1`: validates test-copy ownership and cleanup in a synthetic home
- `pwsh -NoProfile -File Tests/FixedPlacementJourney/Test-AttemptSummary.ps1`: validates separation of active-world and unassigned attempt evidence
- `core-tests`: builds and runs complete-document, per-peer local-world binding and retained-world reconciliation, multiple-MapMod, relationship, track-following, group-activity, scene inventory and presentation coherence, selection, authoring and runtime diagnostic attribution, early repair and exact warning reconciliation tests without game assemblies
- `zizmor --offline .github/workflows/core-tests.yml`: checks the product workflow for locally detectable security findings
- `repository-hygiene` (shared, `v1`): the convention checks of branch names, commit subjects and pull-request titles, the credential scan, workflow validation, Dependabot coverage and the Markdown link check, and `reuse lint`

## Code style

Keep `README.md` user-facing. Describe the product, its audience and its availability; keep development plans, implementation decisions and unresolved development choices at their appropriate owners. Add usage instructions only when they describe established behavior.
