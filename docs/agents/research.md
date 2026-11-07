# Research

A research question always serves something: a map, a spec, an idea, or a problem met in a session. The question states what it serves and points to it, so that the researcher can tell what kind of answer is wanted. When the question is how to solve or design something, it asks first how established systems and practice solve it and whether that fits here, before it asks what is technically possible.

Research skills write "where the repo keeps notes". In this repository that means:

- **Research run:** the invoking workflow first creates the issue, with the question and its closure criteria, and a throwaway branch `research/issue-<issue>-<name>`. The issue carries `planning:research` when it is a child ticket of a `map-the-work` map, and `research:standalone` otherwise. Findings are committed on that branch. The branch is pushed only after the Gitleaks check of `docs/agents/delivery.md` passes, and is never merged. The issue's closing comment holds the findings and the conclusion; the branch is supporting evidence and stays.
- **Fact lookup:** without a research issue or branch in the brief, write the file under `.docs/` and return the findings to the invoking workflow.
- **Sources:** material from outside the repository that the code or skills keep depending on, such as third-party API documentation, goes to `docs/sources/` on the default branch, with its source URL and retrieval date at the top. The researcher flags candidates; the invoking workflow decides and stores them through the delivery strategy.
- **Conclusions:** put them in their home: a spec, an ADR, `GLOSSARY.md` under the receive rules, out-of-scope records, `docs/sources/`, `docs/development/` or `docs/guides/`. A conclusion with no such home stays in the issue. The invoking workflow closes the issue when its closure criteria are met.
