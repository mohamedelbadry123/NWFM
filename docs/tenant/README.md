# Tenant: workspaces and the tenant app

The tenant side is NWFM itself, as one organisation's users see it. Each workspace (tenant) has its own SQL database and its own address. One tenant API serves every workspace and picks the workspace from the request's host name. Workspaces are created on the [platform side](../platform/README.md).

To start or stop the apps, see [Run NWFM locally](../local-startup.md).

## Apps

| App                          | URL                               | Source                          |
| ---------------------------- | --------------------------------- | ------------------------------- |
| Tenant app, workspace `nwfm` | http://localhost:4200             | `frontend` (project `frontend`) |
| Tenant app, other workspaces | http://&lt;key&gt;.localhost:4200 | same app                        |
| Tenant API                   | http://localhost:5081/swagger     | `backend/src/NWFM.Api`          |

`localhost` is the bootstrap workspace `nwfm`, and its database is `NWFM`. A workspace with the key `acme` is at http://acme.localhost:4200. Browsers resolve `*.localhost` to this machine, so no hosts-file entry is needed.

## Sign in to the bootstrap workspace

| URL                   | User                      | Password          |
| --------------------- | ------------------------- | ----------------- |
| http://localhost:4200 | `administrator@localhost` | `Administrator1!` |

The bootstrap workspace seeds this administrator, who holds every permission. Change the password after your first sign-in if the machine is shared.

## First sign-in to a new workspace

After staff approve a signup on the [platform side](../platform/README.md#create-a-workspace-from-a-signup):

1. Open the newest activation e-mail in `data/emails` (repository root) and follow the link at the top.
2. Set the workspace administrator's password.
3. Sign in at http://&lt;key&gt;.localhost:4200 with the administrator's e-mail and that password.

## Logs

| Setup              | Command or place                         |
| ------------------ | ---------------------------------------- |
| Docker             | `docker compose logs -f backend`         |
| Docker, last lines | `docker compose logs --tail 100 backend` |
| `start-dev.bat`    | The **NWFM Backend** window              |

## Related

- [Form engine](../form-engine.md): forms, fields and submissions.
- [Computed columns](../computed-columns.md).
- [Workflow activity forms](../workflow-activity-forms.md).
- [Advanced workflow manual test](../WORKFLOW_ADVANCED_MANUAL_TEST.md).
- [Tenant security](../security/tenant-security.md): how each workspace is isolated.
