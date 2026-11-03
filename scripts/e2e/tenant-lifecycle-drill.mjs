import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

// Every probe uses a third database inside the launcher's disposable SQL container.
// The application tenants stay active. Assertions inspect SQL state after the actual
// operator CLI has completed; no generated success report can replace these checks.
export async function runTenantLifecycleDrill({ containerId, runId, sqlPort, sql }) {
  assert.equal(process.env.NWFM_E2E_ISOLATED, '1');
  assert.match(containerId, /^[a-f0-9]{64}$/);
  assert.match(runId, /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/);
  assert.match(String(sqlPort), /^\d{1,5}$/);
  const ownership = spawnSync(
    'docker',
    ['inspect', '--format', '{{index .Config.Labels "nwfm.e2e.run"}}', containerId],
    { encoding: 'utf8', windowsHide: true, timeout: 10_000 },
  );
  assert.equal(ownership.status, 0, 'The disposable SQL container must exist.');
  assert.equal(ownership.stdout.trim(), runId, 'The lifecycle drill must own its SQL container.');

  const suffix = runId.replaceAll('-', '');
  const target = `NWFM_E2E_LIFECYCLE_${suffix}`;
  const recovery = `NWFM_RECOVERY_${suffix}`;
  const tenant = '10000000-0000-0000-0000-000000000001';
  const foreignTenant = '20000000-0000-0000-0000-000000000002';
  const operator = `lifecycle_operator_${suffix}`;
  const denied = `lifecycle_denied_${suffix}`;
  const operatorPassword = 'E2e!' + randomBytes(24).toString('hex');
  const deniedPassword = 'E2e!' + randomBytes(24).toString('hex');
  const connection = (database, user, secret) =>
    `Server=127.0.0.1,${sqlPort};Database=${database};User Id=${user};Password=${secret};TrustServerCertificate=True;Pooling=False`;
  const operatorConnection = connection(target, operator, operatorPassword);
  const recoveryConnection = connection(recovery, operator, operatorPassword);
  const deniedConnection = connection(target, denied, deniedPassword);
  const cliPath = path.resolve(
    'backend/tools/NWFM.TenantAdmin/bin/Release/net10.0/NWFM.TenantAdmin.dll',
  );

  // A real SQL backup/restore also exercises persisted metadata and identity SIDs.
  // Remove copied request credentials immediately: a recovery copy is never an
  // additional database that an ordinary tenant request identity may access.
  function clone(source, destination, label) {
    const backup = `/var/opt/mssql/data/nwfm-${suffix}-${label}.bak`;
    const data = `/var/opt/mssql/data/nwfm-${suffix}-${label}.mdf`;
    const log = `/var/opt/mssql/data/nwfm-${suffix}-${label}.ldf`;
    sql(
      `BACKUP DATABASE [${source}] TO DISK=N'${backup}' WITH COPY_ONLY, INIT;
       DECLARE @data sysname=(SELECT name FROM sys.master_files WHERE database_id=DB_ID(N'${source}') AND type=0);
       DECLARE @log sysname=(SELECT name FROM sys.master_files WHERE database_id=DB_ID(N'${source}') AND type=1);
       IF @data IS NULL OR @log IS NULL THROW 51200, 'Expected disposable fixture database files.', 1;
       DECLARE @restore nvarchar(max)=N'RESTORE DATABASE [${destination}] FROM DISK=N''${backup}''
           WITH MOVE N'''+REPLACE(@data,N'''',N'''''')+N''' TO N''${data}'',
                MOVE N'''+REPLACE(@log,N'''',N'''''')+N''' TO N''${log}'', RECOVERY;';
       EXEC(@restore);
       EXEC(N'USE [${destination}]; IF USER_ID(N''nwfm_request_A'') IS NOT NULL DROP USER [nwfm_request_A];');`,
      'master',
    );
    sql(
      `EXECUTE AS LOGIN=N'nwfm_request_A';
       IF HAS_DBACCESS(N'${destination}')=1 THROW 51201, 'Request identity can access a lifecycle copy.', 1;
       REVERT;`,
      'master',
    );
  }

  const cli = (args, { admin = operatorConnection, source = recoveryConnection, failure } = {}) =>
    runCli(
      cliPath,
      args,
      {
        NWFM_TENANT_ADMIN_CONNECTION: admin,
        NWFM_TENANT_RECOVERY_CONNECTION: source,
        NWFM_RETENTION_ACTIVE_DAYS: '30',
        NWFM_RETENTION_BACKUP_DAYS: '90',
        NWFM_RETENTION_AUDIT_DAYS: '365',
      },
      failure,
    );

  clone('NWFM_E2E_A', target, 'target');
  sql(
    `CREATE LOGIN [${operator}] WITH PASSWORD=N'${operatorPassword}', CHECK_POLICY=ON;
     CREATE LOGIN [${denied}] WITH PASSWORD=N'${deniedPassword}', CHECK_POLICY=ON;
     CREATE USER [${operator}] FOR LOGIN [${operator}];
     CREATE USER [${denied}] FOR LOGIN [${denied}];
     CREATE ROLE [nwfm_tenant_operator];
     ALTER ROLE [nwfm_tenant_operator] ADD MEMBER [${operator}];
     ${[operator, denied]
       .map(
         (user) => `
       GRANT CONNECT, SELECT, VIEW DEFINITION TO [${user}];
       GRANT UPDATE ON OBJECT::Tenancy.DeploymentBoundary TO [${user}];
       GRANT UPDATE ON OBJECT::Auth.Users TO [${user}];
       GRANT UPDATE ON OBJECT::Auth.RefreshTokens TO [${user}];
       GRANT UPDATE ON OBJECT::Auth.SsoAuthorizationCodes TO [${user}];
       GRANT INSERT ON OBJECT::Tenancy.LifecycleAudit TO [${user}];`,
       )
       .join('\n')}
     SELECT Id, SecurityStamp INTO Tenancy.LifecycleDrillStamps FROM Auth.Users;
     IF NOT EXISTS(SELECT 1 FROM Tenancy.LifecycleDrillStamps)
       THROW 51202, 'Lifecycle drill requires real seeded users.', 1;
     INSERT Auth.RefreshTokens(UserId,TokenHash,ExpiresAtUtc,CreatedAtUtc)
       SELECT CONVERT(nvarchar(450),Id),N'lifecycle-refresh-'+CONVERT(nvarchar(36),Id),
              DATEADD(day,1,SYSUTCDATETIME()),SYSUTCDATETIME() FROM Auth.Users;
     INSERT Auth.SsoAuthorizationCodes(UserId,CodeHash,ExpiresAtUtc,CreatedAtUtc)
       SELECT CONVERT(nvarchar(450),Id),N'lifecycle-sso-'+CONVERT(nvarchar(36),Id),
              DATEADD(day,1,SYSUTCDATETIME()),SYSUTCDATETIME() FROM Auth.Users;`,
    target,
  );

  const assertUnchanged = () =>
    sql(
      `IF NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE IsActive=1 AND TenantId='${tenant}')
       THROW 51203, 'A refused operation suspended the tenant.', 1;
     IF EXISTS(SELECT 1 FROM Tenancy.LifecycleAudit WHERE Action=N'Offboarded')
       THROW 51204, 'A refused operation wrote an offboarding audit.', 1;
     IF EXISTS(SELECT 1 FROM Auth.RefreshTokens WHERE TokenHash LIKE N'lifecycle-refresh-%' AND RevokedAtUtc IS NOT NULL)
       OR EXISTS(SELECT 1 FROM Auth.SsoAuthorizationCodes WHERE CodeHash LIKE N'lifecycle-sso-%' AND ConsumedAtUtc IS NOT NULL)
       THROW 51205, 'A refused operation revoked a session.', 1;
     IF EXISTS(SELECT 1 FROM Auth.Users u JOIN Tenancy.LifecycleDrillStamps s ON u.Id=s.Id
       WHERE ISNULL(u.SecurityStamp,N'')<>ISNULL(s.SecurityStamp,N''))
       THROW 51206, 'A refused operation changed identity stamps.', 1;`,
      target,
    );
  await cli(['offboard', tenant, 'Unauthorized operator probe'], {
    admin: deniedConnection,
    failure: 'Tenant operation failed.',
  });
  assertUnchanged();
  await cli(['offboard', foreignTenant, 'Wrong tenant probe'], {
    failure: 'Tenant operation failed.',
  });
  assertUnchanged();

  // A failure after revocation must roll back the entire offboarding transaction.
  sql(`DENY INSERT ON OBJECT::Tenancy.LifecycleAudit TO [${operator}];`, target);
  await cli(['offboard', tenant, 'Atomic rollback probe'], { failure: 'Tenant operation failed.' });
  assertUnchanged();
  sql(
    `REVOKE INSERT ON OBJECT::Tenancy.LifecycleAudit FROM [${operator}];
       GRANT INSERT ON OBJECT::Tenancy.LifecycleAudit TO [${operator}];`,
    target,
  );

  clone(target, recovery, 'recovery');
  sql(`DROP USER [${denied}];`, recovery);
  sql(
    `ALTER DATABASE [${recovery}] SET READ_ONLY;
       ALTER DATABASE [${recovery}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;`,
    'master',
  );
  const storage = path.resolve('.e2e/media', runId, 'lifecycle-recovery');
  const foreignStorage = path.resolve('.e2e/media', runId, 'lifecycle-foreign');
  await mkdir(storage, { recursive: true });
  await mkdir(foreignStorage, { recursive: true });
  await writeFile(path.join(storage, '.nwfm-tenant'), tenant, { flag: 'wx' });
  await writeFile(path.join(foreignStorage, '.nwfm-tenant'), foreignTenant, { flag: 'wx' });

  const offboarded = await cli(['offboard', tenant, 'Disposable lifecycle drill', '--legal-hold']);
  assert.equal(offboarded.TenantId, tenant);
  assert.equal(offboarded.LegalHold, true);
  sql(
    `IF NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE IsActive=0 AND TenantId='${tenant}')
       THROW 51207, 'Offboarding did not durably suspend the tenant.', 1;
     IF EXISTS(SELECT 1 FROM Auth.RefreshTokens WHERE RevokedAtUtc IS NULL)
       OR EXISTS(SELECT 1 FROM Auth.SsoAuthorizationCodes WHERE ConsumedAtUtc IS NULL)
       THROW 51208, 'Offboarding left active refresh or SSO sessions.', 1;
     IF EXISTS(SELECT 1 FROM Auth.Users u JOIN Tenancy.LifecycleDrillStamps s ON u.Id=s.Id
       WHERE ISNULL(u.SecurityStamp,N'')=ISNULL(s.SecurityStamp,N'')
         OR u.LockoutEnabled<>1 OR u.LockoutEnd IS NULL OR YEAR(u.LockoutEnd)<>9999)
       THROW 51209, 'Offboarding did not revoke and lock every identity.', 1;
     IF (SELECT COUNT(*) FROM Tenancy.LifecycleAudit WHERE Action=N'Offboarded'
       AND TenantId='${tenant}' AND OperatorLogin=N'${operator}' AND Reason=N'Disposable lifecycle drill'
       AND LegalHold=1 AND DATEDIFF(day,OccurredAtUtc,ActiveDataDueUtc)=30
       AND DATEDIFF(day,OccurredAtUtc,BackupDueUtc)=90
       AND DATEDIFF(day,OccurredAtUtc,AuditDueUtc)=365)<>1
       THROW 51210, 'Durable operator attribution or retention policy is incorrect.', 1;`,
    target,
  );
  const held = await cli(['retention', tenant]);
  assert.equal(held.Status, 'OnHold');
  assert.equal(held.Schedule.LegalHold, true);
  await cli(['retention', tenant], {
    admin: deniedConnection,
    failure: 'Tenant operation failed.',
  });
  await cli(['offboard', tenant, 'Duplicate offboard probe'], {
    failure: 'Tenant operation failed.',
  });

  // The operator can append evidence but cannot rewrite or remove earlier evidence.
  sql(
    "UPDATE Tenancy.LifecycleAudit SET Reason=N'tampered';",
    target,
    operator,
    operatorPassword,
    'Msg 229',
  );
  sql('DELETE FROM Tenancy.LifecycleAudit;', target, operator, operatorPassword, 'Msg 229');

  const verifyArgs = [
    'verify-restore',
    tenant,
    storage,
    `fixture-backup-${suffix}`,
    'Disposable recovery drill',
  ];
  const verified = await cli(verifyArgs);
  assert.equal(verified.OwnershipVerified, true);
  sql(
    `IF (SELECT COUNT(*) FROM Tenancy.LifecycleAudit WHERE Action=N'RestoreOwnershipVerified'
       AND TenantId='${tenant}' AND OperatorLogin=N'${operator}' AND RecoveryDatabase=N'${recovery}')<>1
       THROW 51211, 'Recovery ownership verification was not audited.', 1;
     IF NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE IsActive=0)
       OR EXISTS(SELECT 1 FROM Auth.RefreshTokens WHERE RevokedAtUtc IS NULL)
       THROW 51212, 'Verification restored stale access or sessions.', 1;`,
    target,
  );
  sql(
    `IF NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE IsActive=1)
       OR NOT EXISTS(SELECT 1 FROM Auth.RefreshTokens WHERE RevokedAtUtc IS NULL)
       THROW 51213, 'The recovery fixture must retain its real pre-offboarding state.', 1;`,
    recovery,
  );

  await cli(
    ['verify-restore', tenant, foreignStorage, 'foreign-storage', 'Foreign storage probe'],
    {
      failure: 'The recovery storage root does not belong to the target tenant.',
    },
  );
  await cli(verifyArgs, {
    source: operatorConnection,
    failure: 'A live tenant database cannot be its recovery source.',
  });
  sql(`ALTER DATABASE [${recovery}] SET MULTI_USER WITH ROLLBACK IMMEDIATE;`, 'master');
  await cli(verifyArgs, { failure: 'Recovery verification requires a separately restored' });
  sql(`ALTER DATABASE [${recovery}] SET READ_WRITE;`, 'master');
  sql(
    `CREATE TABLE Tenancy.ForeignOwnershipProbe(OrganizationId uniqueidentifier NULL);
       INSERT Tenancy.ForeignOwnershipProbe VALUES('${foreignTenant}');`,
    recovery,
  );
  sql(
    `ALTER DATABASE [${recovery}] SET READ_ONLY;
       ALTER DATABASE [${recovery}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;`,
    'master',
  );
  await cli(verifyArgs, { failure: 'Tenant operation failed.' });
  sql(
    `ALTER DATABASE [${recovery}] SET MULTI_USER WITH ROLLBACK IMMEDIATE;
       ALTER DATABASE [${recovery}] SET READ_WRITE;`,
    'master',
  );
  sql('UPDATE Tenancy.ForeignOwnershipProbe SET OrganizationId=NULL;', recovery);
  sql(
    `ALTER DATABASE [${recovery}] SET READ_ONLY;
       ALTER DATABASE [${recovery}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;`,
    'master',
  );
  await cli(verifyArgs, { failure: 'Tenant operation failed.' });
  sql(
    `IF (SELECT COUNT(*) FROM Tenancy.LifecycleAudit WHERE Action=N'RestoreOwnershipDenied'
       AND TenantId='${tenant}' AND OperatorLogin=N'${operator}')<>5
       THROW 51214, 'Recovery denials were not durably attributed.', 1;
     IF (SELECT COUNT(*) FROM Tenancy.LifecycleAudit WHERE Action=N'Offboarded' AND LegalHold=1)<>1
       OR NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE IsActive=0)
       THROW 51215, 'Denied recovery changed the offboarding or legal hold.', 1;`,
    target,
  );
  assert.equal((await cli(['retention', tenant])).Status, 'OnHold');
  // Both source application tenants remain available; cleanup owns the complete container.
  sql(
    `IF NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE IsActive=1)
       THROW 51216, 'Lifecycle drill changed the live test tenant.', 1;`,
    'NWFM_E2E_A',
  );
  console.log(
    'Lifecycle SQL drill passed: operator denial, atomic revocation, legal hold, isolated restore and ownership denials.',
  );
}

async function runCli(cliPath, args, environment, failure) {
  return await new Promise((resolve, reject) => {
    const child = spawn('dotnet', [cliPath, ...args], {
      env: { ...process.env, ...environment },
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });
    let output = '';
    for (const stream of [child.stdout, child.stderr])
      stream.on('data', (chunk) => {
        output = (output + chunk.toString()).slice(-65_536);
      });
    const stop = () => child.kill();
    process.once('SIGINT', stop);
    process.once('SIGTERM', stop);
    const timer = setTimeout(() => {
      stop();
      reject(new Error('The isolated lifecycle command timed out.'));
    }, 90_000);
    const dispose = () => {
      clearTimeout(timer);
      process.removeListener('SIGINT', stop);
      process.removeListener('SIGTERM', stop);
    };
    child.once('error', () => {
      dispose();
      reject(new Error('Could not start the isolated lifecycle command.'));
    });
    child.once('exit', (code) => {
      dispose();
      // Never include raw connection diagnostics, passwords or child output in failures.
      if (failure) {
        if (code !== 0 && output.includes(failure)) resolve(undefined);
        else reject(new Error(`Lifecycle ${args[0]} did not refuse the forbidden operation.`));
      } else if (code !== 0) reject(new Error(`Lifecycle ${args[0]} failed.`));
      else {
        try {
          resolve(JSON.parse(output.trim()));
        } catch {
          reject(new Error(`Lifecycle ${args[0]} did not return its expected result.`));
        }
      }
    });
  });
}
