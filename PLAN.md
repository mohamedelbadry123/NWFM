# NWFM approved implementation scope

Approved on 2026-09-16. This document supersedes the original proposal that included authentication.

## Product

- One .NET 10 backend and one Angular 21 frontend, selectively extracted from the Workflow module in D:/Work/Privora-New-Tech-Stach.
- Full existing Workflow capability and UI, English/Arabic support, and NWFM branding.
- One default tenant in v1; preserve tenant identifiers, scoped persistence, and background processing.
- Direct startup without login. A visible participant selector controls task attribution.
- No Identity, authentication, passwords, JWTs, permissions, platform administration, or privacy/compliance business modules.

## Implementation

1. Extract Workflow Domain, Application, Infrastructure, and API plus required shared primitives.
2. Provide configured tenant context and participant-based actor context with replaceable interfaces for future authentication.
3. Register participants directly, without creating user accounts.
4. Preserve workflow designer, versioning, bindings, runtime, tasks, assignments, SLA, calendars, history, notifications, incidents, and recovery.
5. Replace business catalogs and record panels with a Standalone Workflow catalog, generic request context, manual start, and a standalone outcome handler.
6. Create fresh SQL migrations and repeatable initialization for one tenant, two reviewers, one group, and one approval example.
7. Regenerate the reduced API client; provide local startup and container configuration.

## Verification

- Build both applications and run retained backend/frontend workflow tests.
- Test default context, participant selection, direct registration, and rejection of invalid tenant/participant values.
- Start, claim, complete, and inspect a workflow in the browser and through automated HTTP checks.
- Check concurrent claims, duplicate starts, task ownership, version pinning, and tenant-separated parent/child records.
- Check excluded modules are absent from the API and application navigation.

See README.md for startup, extension points, and verification commands. The source project is unchanged; this project uses its own NWFM database.
