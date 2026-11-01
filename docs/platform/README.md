# Platform: control plane, signup and staff console

The platform side runs NWFM as a service. It holds the catalog of workspaces (tenants), takes signups, and lets staff approve, migrate, suspend and support workspaces. It never serves a workspace's own users; the [tenant side](../tenant/README.md) does that.

To start or stop the apps, see [Run NWFM locally](../local-startup.md).

## Apps

| App                     | URL                           | Source                           |
| ----------------------- | ----------------------------- | -------------------------------- |
| Landing page and signup | http://localhost:4300         | `frontend/projects/platform-web` |
| Staff console           | http://localhost:4300/console | `frontend/projects/platform-web` |
| Platform API            | http://localhost:5180/swagger | `backend/src/NWFM.Platform.Api`  |

The platform API owns the `NWFM_Catalog` database, and is the only app that migrates databases. On start it migrates the catalog and the bootstrap workspace `nwfm`.

## Staff sign-in

| User                 | Password          |
| -------------------- | ----------------- |
| `platform@localhost` | `PlatformAdmin1!` |

This account is created only when the catalog has no staff account. If Docker created the catalog, the password is `NWFM_PLATFORM_ADMIN_PASSWORD` in `.env` instead. Change it after your first sign-in if the machine is shared.

## Create a workspace from a signup

1. **Sign up.** Open http://localhost:4300 and sign up an organisation with a key, for example `acme`. Choose a database we provide, or bring your own SQL Server.
2. **Verify the e-mail.** Open the newest `Confirm your e-mail…` file in `data/emails` and follow the link at the top.
3. **Approve.** Sign in to http://localhost:4300/console and approve the request under **Signups**. The platform creates and migrates the workspace's database.
4. **Activate.** An activation e-mail for the workspace administrator appears in `data/emails`. Continue on the [tenant side](../tenant/README.md#first-sign-in-to-a-new-workspace).

When the platform runs in Docker, register a database server for managed signups as `host.docker.internal`, not `localhost`.

## E-mails

The platform API doesn't send real e-mail locally. It writes every e-mail to a file:

**`data/emails`** in the repository root (with Docker, this folder is mounted into the container)

- **File names.** Each file is named `<date>-<time>-<subject>-<id>.txt`, so the newest sorts last.
- **Links.** Each file lists its links at the top, under `Links:`, followed by the HTML body.
- **What's in the folder.** Signup verification and workspace activation e-mails are both written here.

## Logs

| Setup              | Command or place                          |
| ------------------ | ----------------------------------------- |
| Docker             | `docker compose logs -f platform`         |
| Docker, last lines | `docker compose logs --tail 100 platform` |
| `start-dev.bat`    | The **NWFM Platform API** window          |

## Related

- [Tenant lifecycle and recovery operations](tenant-lifecycle-operations.md): migrate, suspend, offboard, recover and support access.
- [Tenant security](../security/tenant-security.md): isolation model, credentials and production rollout.
