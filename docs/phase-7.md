# Phase 7 assessment release evidence

The [reviewer guide](reviewer-guide.md) gives public repository links, clean setup, a role/API walkthrough, source-archive instructions and honest limitations. The [architecture](architecture.md), [requirements](requirements.md), [decisions](decisions.md), [backlog](backlog.md) and API contract are linked from the README. The web repository provides English and Arabic report screenshots from an isolated test fixture.

Phase 6 already established a [tested, digest-pinned image pair](../releases/phase-6-tested.json) with Linux-runner clean start, non-root identities, HTTPS health, restart persistence and SQLite backup/restore. The API and web main CI workflows independently cover application checks and image publication. Phase 7 adds a source-only ZIP for each repository, with recorded Git commit and SHA-256. The archives are prepared for review or an assessment email, but no email, invitation or supplier message is sent by this phase.

The final fresh-checkout run and archive hashes will be recorded here after both Phase 7 documentation PRs merge. Azure URL and deployment evidence await Phase 8 cost approval.
