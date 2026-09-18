const fs=require('fs'),path=require('path');
function walk(p){return fs.readdirSync(p,{withFileTypes:true}).flatMap(e=>e.isDirectory()?walk(path.join(p,e.name)):[path.join(p,e.name)])}
const src='D:/Work/Privora-New-Tech-Stach/frontend';
for(const f of walk(src+'/src/app/features/workflow').filter(p=>p.endsWith('.spec.ts'))){const dest='frontend/'+path.relative(src,f);fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,fs.readFileSync(f,'utf8').replaceAll('Privora','NWFM'));}
fs.copyFileSync(src+'/karma.conf.js','frontend/karma.conf.js');
const lock=JSON.parse(fs.readFileSync('frontend/package-lock.json'));lock.name='nwfm-frontend';lock.packages[''].name='nwfm-frontend';fs.writeFileSync('frontend/package-lock.json',JSON.stringify(lock,null,2));
const settings=JSON.parse(fs.readFileSync('backend/src/NWFM.Api/appsettings.json'));settings.ConnectionStrings.DefaultConnection=settings.ConnectionStrings.DefaultConnection.replace(';MultipleActiveResultSets=True','');fs.writeFileSync('backend/src/NWFM.Api/appsettings.json',JSON.stringify(settings,null,2));
for(const p of ['backend/src/Modules/Workflow/Workflow.Infrastructure/Persistence/Configurations/WorkflowRequestConfiguration.cs','backend/src/Modules/Workflow/Workflow.Infrastructure/Persistence/Configurations/WorkflowNotificationLogConfiguration.cs']){let s=fs.readFileSync(p,'utf8');s=s.replace('.HasDefaultValue(WorkflowInstanceStatus.Running);','.HasDefaultValue(WorkflowInstanceStatus.Running).HasSentinel((WorkflowInstanceStatus)(-1));').replace('.HasDefaultValue(WorkflowNotificationLogStatus.Logged);','.HasDefaultValue(WorkflowNotificationLogStatus.Logged).HasSentinel((WorkflowNotificationLogStatus)(-1));');fs.writeFileSync(p,s);}
// The permission catalog is unused because access control is explicitly outside v1.
fs.unlinkSync('backend/src/Modules/Workflow/Workflow.Application/Constants/WorkflowPermissions.cs');
fs.writeFileSync('PLAN.md',`# NWFM approved implementation scope

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
`);
console.log('Documentation, test assets, and configuration finalized.');
