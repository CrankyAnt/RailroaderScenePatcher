# Domain docs

How the engineering skills consume this repository's domain documentation.

**Layout:** single-context

## Before exploring, read these

- `GLOSSARY.md` at the repository root.
- `docs/adr/`: read ADRs that touch the area being explored.
- `SCOPE.md` at the repository root: read the product's purpose and non-goals when writing or recommending new work. During delivery the contract bounds the builder instead; triage applies this file to incidental findings.

If any of these files do not exist, proceed silently. The `model-domain` skill, reached through `grill-and-document`, creates terms and decisions when they are resolved. `SCOPE.md` changes only through `grill-and-document`.

## Use the glossary's vocabulary

When output names a domain concept, use the term as defined in `GLOSSARY.md`.

If a needed concept is absent, reconsider whether it belongs to the product or note the gap for `model-domain`.

## Flag ADR conflicts

Surface any contradiction with an existing ADR explicitly.

## Flag work outside the scope

Work belongs to the product only when it serves the purpose in `SCOPE.md` and hits none of its non-goals. Put work outside that boundary to the user as one question: a conscious extension, or not. Never extend or reject the scope silently.
