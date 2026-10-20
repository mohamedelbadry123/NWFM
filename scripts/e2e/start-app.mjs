import { spawn, spawnSync } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import net from 'node:net';
import { setTimeout as delay } from 'node:timers/promises';
import { runTenantLifecycleDrill } from './tenant-lifecycle-drill.mjs';
import { createTenantProxy } from './tenant-proxy.mjs';

if (process.env.NWFM_E2E_ISOLATED !== '1')
  throw new Error('This launcher requires the isolated e2e config.');
const runId = randomUUID();
const containerName = 'nwfm-e2e-' + runId;
let containerId;
let stopping = false;
const children = [];
const proxies = [];
const password = 'E2e!' + randomBytes(24).toString('hex');
const signingKey = randomBytes(48).toString('base64');
const fixtureUsers = [
  'security-admin',
  'security-reviewer',
  'security-disabled',
  'forms-routing',
  'forms-designer',
  'forms-publish',
  'forms-preview',
  'forms-history',
  'forms-claim',
].map((name) => name + '@localhost');
const dockerEnv = { ...process.env, MSSQL_SA_PASSWORD: password };
function docker(args, optional = false) {
  const result = spawnSync('docker', args, { encoding: 'utf8', env: dockerEnv, timeout: 180_000 });
  if (!optional && (result.error || result.status !== 0))
    throw new Error('Test Docker operation failed: ' + (result.error?.message ?? result.stderr));
  return result;
}
async function cleanup(code) {
  if (stopping) return;
  stopping = true;
  for (const proxy of proxies) proxy.close();
  for (const child of children) {
    if (child.exitCode === null && child.signalCode === null) {
      try {
        if (process.platform === 'win32') {
          spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], {
            windowsHide: true,
            stdio: 'ignore',
          });
        } else process.kill(-child.pid, 'SIGTERM');
      } catch {
        /* already exited */
      }
    }
  }
  await Promise.race([
    Promise.all(
      children.map(
        (child) =>
          new Promise((resolve) => {
            if (child.exitCode !== null || child.signalCode !== null) resolve();
            else child.once('exit', resolve);
          }),
      ),
    ),
    delay(5000),
  ]);
  for (const child of children) {
    if (child.exitCode === null && child.signalCode === null) {
      try {
        process.kill(-child.pid, 'SIGKILL');
      } catch {
        /* already exited */
      }
    }
  }
  if (containerId) {
    const owned = docker(
      ['inspect', '--format', '{{index .Config.Labels "nwfm.e2e.run"}}', containerId],
      true,
    );
    if (owned.status === 0 && owned.stdout.trim() === runId) {
      const removed = docker(['rm', '--force', containerId], true);
      if (removed.status !== 0) {
        console.error('Could not remove owned e2e container ' + containerId);
        code = 1;
      }
    } else {
      console.error('Cannot verify ownership of test container; refusing removal.');
      code = 1;
    }
  }
  process.exit(code);
}
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => void cleanup(0));
function start(command, args, env = {}, cwd = process.cwd()) {
  const child = spawn(command, args, {
    env: { ...process.env, ...env },
    stdio: 'inherit',
    detached: true,
    windowsHide: true,
    cwd,
  });
  children.push(child);
  child.on('error', (error) => {
    console.error(error.message);
    void cleanup(1);
  });
  child.on('exit', (code) => {
    if (!stopping) {
      console.error(command + ' exited unexpectedly: ' + code);
      void cleanup(1);
    }
  });
  return child;
}
function runToExit(command, args, env, expectedFailure) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      env: { ...process.env, ...env },
      stdio: expectedFailure ? 'pipe' : 'inherit',
      detached: true,
      windowsHide: true,
    });
    children.push(child);
    // Expected refusal output contains environment details; keep it out of artifacts.
    let output = '';
    for (const stream of [child.stdout, child.stderr]) {
      stream?.on('data', (chunk) => {
        output = (output + chunk.toString()).slice(-1_000_000);
      });
    }
    const timeout = setTimeout(() => {
      child.kill();
      reject(new Error('Isolated database initialization or refusal check timed out.'));
    }, 240_000);
    child.on('error', (error) => {
      clearTimeout(timeout);
      reject(error);
    });
    child.on('exit', (code) => {
      clearTimeout(timeout);
      if (expectedFailure ? code !== 0 && output.includes(expectedFailure) : code === 0) resolve();
      else {
        const diagnostic = output
          .split(/\r?\n/)
          .filter((line) => /Unhandled exception|SqlException|InvalidOperationException/.test(line))
          .slice(0, 3)
          .join(' ')
          .replaceAll(password, '[redacted]')
          .replaceAll(signingKey, '[redacted]')
          .replace(/E2e![0-9a-f]+/g, '[redacted]');
        reject(
          new Error(
            'Isolated startup check failed' +
              (expectedFailure ? ' (expected: ' + expectedFailure + ')' : '') +
              (diagnostic ? ': ' + diagnostic : '.'),
          ),
        );
      }
    });
  });
}
function sql(script, database, username = 'sa', sqlPassword = password, expectedFailure) {
  const result = spawnSync(
    'docker',
    [
      'exec',
      '--interactive',
      '--env',
      'SQLCMDPASSWORD',
      containerId,
      '/opt/mssql-tools18/bin/sqlcmd',
      '-S',
      'localhost',
      '-U',
      username,
      '-d',
      database,
      '-C',
      '-I',
      '-b',
    ],
    {
      env: { ...process.env, SQLCMDPASSWORD: sqlPassword },
      input: script,
      encoding: 'utf8',
      timeout: 60_000,
      windowsHide: true,
    },
  );
  const output = (result.stdout ?? '') + (result.stderr ?? '');
  if (
    result.error ||
    (expectedFailure
      ? result.status === 0 || !output.includes(expectedFailure)
      : result.status !== 0)
  )
    throw new Error(
      'Isolated SQL privilege check failed: ' +
        (result.error?.message ?? output)
          .replaceAll(password, '[redacted]')
          .replaceAll(sqlPassword, '[redacted]')
          .replace(/E2e![0-9a-f]+/g, '[redacted]'),
    );
  return output;
}
async function freePort(port) {
  await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once('error', () =>
      reject(
        new Error(
          'Port ' +
            port +
            ' is occupied. Stop the existing service before e2e; it will not be reused or stopped automatically.',
        ),
      ),
    );
    server.listen(port, '127.0.0.1', () => server.close(resolve));
  });
}
async function ready(check, label) {
  const deadline = Date.now() + 180_000;
  while (Date.now() < deadline && !stopping) {
    if (await check()) return;
    await delay(500);
  }
  throw new Error(label + ' did not become ready.');
}
try {
  for (const port of [4200, 4201, 5081, 5082, 5091, 2525]) await freePort(port);
  containerId = docker([
    'create',
    '--name',
    containerName,
    '--label',
    'nwfm.e2e.run=' + runId,
    '--env',
    'ACCEPT_EULA=Y',
    '--env',
    'MSSQL_SA_PASSWORD',
    '--publish',
    '127.0.0.1::1433',
    'mcr.microsoft.com/mssql/server:2022-latest',
  ]).stdout.trim();
  docker(['start', containerId]);
  const mapped = docker(['port', containerId, '1433/tcp']).stdout.trim();
  const sqlPort = /^127\.0\.0\.1:(\d+)$/.exec(mapped)?.[1];
  if (!sqlPort) throw new Error('Unexpected SQL port mapping.');
  await ready(
    async () =>
      docker(
        [
          'exec',
          containerId,
          '/bin/sh',
          '-c',
          'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -Q "SELECT 1" -b -o /dev/null',
        ],
        true,
      ).status === 0,
    'SQL Server',
  );
  start('node', ['scripts/workflow-demo-transports.mjs'], {
    DEMO_HTTP_PORT: '5091',
    DEMO_SMTP_PORT: '2525',
  });
  await ready(async () => {
    try {
      const response = await fetch('http://127.0.0.1:5091/health', {
        signal: AbortSignal.timeout(2000),
      });
      return response.ok && (await response.text()) === 'ok';
    } catch {
      return false;
    }
  }, 'Local HTTP transport');
  await ready(
    () =>
      new Promise((resolve) => {
        const socket = net.connect(2525, '127.0.0.1');
        socket.setTimeout(2000);
        socket.once('data', (banner) => {
          socket.destroy();
          resolve(banner.toString().startsWith('220 '));
        });
        socket.once('error', () => resolve(false));
        socket.once('timeout', () => {
          socket.destroy();
          resolve(false);
        });
      }),
    'Local SMTP transport',
  );
  await runToExit('dotnet', ['build', 'backend/src/NWFM.Api', '--configuration', 'Release'], {});
  await runToExit(
    'dotnet',
    ['build', 'backend/tools/NWFM.TenantAdmin', '--configuration', 'Release'],
    {},
  );
  const deployments = [
    { suffix: 'A', tenantId: '10000000-0000-0000-0000-000000000001', port: 5081 },
    { suffix: 'B', tenantId: '20000000-0000-0000-0000-000000000002', port: 5082 },
  ];
  for (const deployment of deployments) {
    const database = 'NWFM_E2E_' + deployment.suffix;
    const requestUser = 'nwfm_request_' + deployment.suffix;
    const requestPassword = 'E2e!' + randomBytes(24).toString('hex');
    const connection = (user, secret) =>
      `Server=127.0.0.1,${sqlPort};Database=${database};User Id=${user};Password=${secret};TrustServerCertificate=True;Max Pool Size=20`;
    const args = [
      'backend/src/NWFM.Api/bin/Release/net10.0/NWFM.Api.dll',
      '--contentRoot',
      process.cwd() + '/backend/src/NWFM.Api',
      '--urls',
      'http://127.0.0.1:' + deployment.port,
    ];
    const env = {
      ASPNETCORE_ENVIRONMENT: 'Development',
      Application__TenantId: deployment.tenantId,
      ConnectionStrings__DefaultConnection: connection('sa', password),
      JwtSettings__SigningKey: signingKey,
      CorsOrigins__0: 'http://localhost:4200',
      CorsOrigins__1: 'http://localhost:4201',
      DatabaseStartup__InitializeOnly: 'true',
      DatabaseStartup__ApplyMigrations: 'true',
      DatabaseStartup__SeedData: 'true',
      DatabaseStartup__ApplySqlObjects: 'true',
      WorkflowDemo__Enabled: 'true',
      WorkflowDemo__HttpAddress: 'http://127.0.0.1:5091',
      WorkflowDemo__SmtpPort: '2525',
      WorkflowDemo__CallbackKey: randomBytes(32).toString('hex'),
      WorkflowSettings__AllowPrivateConnections: 'true',
      WorkflowSettings__MaxPendingPerQueue: deployment.suffix === 'B' ? '8' : '10000',
      Sso__Enabled: 'false',
      ActiveDirectory__Enabled: 'false',
      TeamOtp__Enabled: 'false',
      C2m__Enabled: 'false',
      FileStorage__Root: process.cwd() + '/.e2e/media/' + runId + '/' + deployment.suffix,
      CacheSettings__Provider: 'InMemory',
      Logging__LogLevel__Default: 'Warning',
    };
    await runToExit('dotnet', args, env);
    if (deployment.suffix === 'A') {
      await runToExit('dotnet', args, {
        ...env,
        DatabaseStartup__ApplyMigrations: 'false',
        DatabaseStartup__SeedData: 'false',
        DatabaseStartup__ApplySqlObjects: 'false',
        WorkflowDemo__Enabled: 'false',
      });
    }
    // Disposable accounts reuse only the fixture's development password hash. No production
    // account, external credential, or authentication bypass endpoint is involved.
    sql(
      `INSERT Auth.Users
       (Id,UserName,NormalizedUserName,Email,NormalizedEmail,EmailConfirmed,
        PasswordHash,SecurityStamp,ConcurrencyStamp,PhoneNumberConfirmed,
        TwoFactorEnabled,LockoutEnabled,AccessFailedCount)
       SELECT NEWID(), fixtures.UserName, UPPER(fixtures.UserName),
              fixtures.UserName, UPPER(fixtures.UserName), 1,
              admin.PasswordHash, CONVERT(nvarchar(36),NEWID()),
              CONVERT(nvarchar(36),NEWID()), 0, 0, 1, 0
       FROM Auth.Users admin CROSS JOIN
            (VALUES ${fixtureUsers.map((name) => `('${name}')`).join(',')}) fixtures(UserName)
       WHERE admin.NormalizedUserName=N'ADMINISTRATOR@LOCALHOST';
       INSERT Auth.UserRoles(UserId,RoleId)
       SELECT users.Id, roles.Id FROM Auth.Users users CROSS JOIN Auth.Roles roles
       WHERE users.NormalizedUserName IN (${fixtureUsers.map((name) => `N'${name.toUpperCase()}'`).join(',')})
         AND roles.NormalizedName=N'ADMINISTRATOR';`,
      database,
    );
    // Explicitly bounded schema grants preserve dynamic form publication while excluding
    // the immutable deployment boundary and every other tenant database.
    sql(
      `CREATE LOGIN [${requestUser}] WITH PASSWORD=N'${requestPassword}', CHECK_POLICY=ON;
       CREATE USER [${requestUser}] FOR LOGIN [${requestUser}];
       GRANT CONNECT TO [${requestUser}];
       GRANT SELECT ON OBJECT::[Tenancy].[DeploymentBoundary] TO [${requestUser}];
       GRANT VIEW DEFINITION ON OBJECT::[Tenancy].[LifecycleAudit] TO [${requestUser}];
       GRANT VIEW DEFINITION ON USER::[nwfm_form_storage_owner] TO [${requestUser}];
       ${['Auth', 'FormEngine', 'Task', 'Workflow']
         .map(
           (schema) =>
             `GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[${schema}] TO [${requestUser}];`,
         )
         .join('\n')}
       GRANT CREATE TABLE TO [${requestUser}];
       GRANT ALTER ON SCHEMA::[FormEngine] TO [${requestUser}];`,
      database,
    );
    const runtime = {
      ...env,
      ConnectionStrings__DefaultConnection: connection(requestUser, requestPassword),
      DatabaseStartup__InitializeOnly: 'false',
      DatabaseStartup__ApplyMigrations: 'false',
      DatabaseStartup__SeedData: 'false',
      DatabaseStartup__ApplySqlObjects: 'false',
      WorkflowDemo__Enabled: 'false',
    };
    // A passing healthy deployment alone cannot prove these controls fail closed.
    await runToExit(
      'dotnet',
      args,
      {
        ...runtime,
        ConnectionStrings__DefaultConnection: connection('sa', password),
      },
      'The request database identity has administrative privileges.',
    );
    await runToExit(
      'dotnet',
      args,
      {
        ...runtime,
        Application__TenantId: randomUUID(),
      },
      'Database tenant binding is missing or mismatched.',
    );
    await runToExit(
      'dotnet',
      args,
      {
        ...env,
        Application__TenantId: randomUUID(),
      },
      'Database belongs to another tenant.',
    );
    sql(
      `IF (SELECT COUNT(*) FROM Tenancy.LifecycleAudit WHERE Action=N'Provisioned')<>1
         OR NOT EXISTS(SELECT 1 FROM Tenancy.LifecycleAudit
           WHERE Action=N'Provisioned' AND TenantId='${deployment.tenantId}' AND OperatorLogin=N'sa')
       THROW 51219, 'Provisioning must write exactly one attributed tenant lifecycle event.', 1;`,
      database,
    );
    sql(`DENY VIEW ANY DATABASE TO [${requestUser}];`, 'master');
    await runToExit(
      'dotnet',
      args,
      runtime,
      'The request identity cannot verify the database isolation inventory.',
    );
    sql(`REVOKE VIEW ANY DATABASE FROM [${requestUser}];`, 'master');
    sql(`GRANT IMPERSONATE ON LOGIN::[sa] TO [${requestUser}];`, 'master');
    await runToExit(
      'dotnet',
      args,
      runtime,
      'The request identity can impersonate another server login.',
    );
    sql(`REVOKE IMPERSONATE ON LOGIN::[sa] FROM [${requestUser}];`, 'master');
    if (deployment.suffix === 'B') {
      await runToExit(
        'dotnet',
        args,
        {
          ...runtime,
          FileStorage__Root: process.cwd() + '/.e2e/media/' + runId + '/A',
        },
        'The file storage root belongs to another tenant or has an invalid tenant binding.',
      );
    }
    start('dotnet', args, runtime);
    await ready(async () => {
      try {
        const response = await fetch('http://127.0.0.1:' + deployment.port + '/health', {
          signal: AbortSignal.timeout(2000),
        });
        return response.ok && (await response.json()).application === 'NWFM';
      } catch {
        return false;
      }
    }, 'NWFM tenant ' + deployment.suffix);
    deployment.database = database;
    deployment.requestUser = requestUser;
    deployment.requestPassword = requestPassword;
  }
  for (const [own, other] of [deployments, [...deployments].reverse()]) {
    sql('SELECT COUNT(*) FROM Auth.Users;', own.database, own.requestUser, own.requestPassword);
    sql(
      `SELECT COUNT(*) FROM [${other.database}].[Auth].[Users];`,
      own.database,
      own.requestUser,
      own.requestPassword,
      'Msg 916',
    );
    sql(
      'UPDATE Tenancy.DeploymentBoundary SET TenantId=NEWID();',
      own.database,
      own.requestUser,
      own.requestPassword,
      'Msg 229',
    );
    // Dynamic form DDL must not make a trigger an ownership-chain escalation path.
    sql(
      `CREATE TABLE FormEngine.TenantBoundaryProbe(Id int NOT NULL);
       GO
       CREATE TRIGGER FormEngine.TenantBoundaryProbeTrigger
         ON FormEngine.TenantBoundaryProbe AFTER INSERT AS
         UPDATE Tenancy.DeploymentBoundary SET TenantId=NEWID();
       GO`,
      own.database,
      own.requestUser,
      own.requestPassword,
    );
    sql(
      'INSERT FormEngine.TenantBoundaryProbe(Id) VALUES(1);',
      own.database,
      own.requestUser,
      own.requestPassword,
      'Msg 229',
    );
    sql(
      `IF NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE TenantId='${own.tenantId}')
       THROW 51217, 'Dynamic form DDL bypassed the tenant binding boundary.', 1;
       IF EXISTS(SELECT 1 FROM FormEngine.TenantBoundaryProbe)
       THROW 51218, 'A denied trigger insert was not rolled back.', 1;`,
      own.database,
    );
    sql(
      'DROP TABLE FormEngine.TenantBoundaryProbe;',
      own.database,
      own.requestUser,
      own.requestPassword,
    );
  }
  await runTenantLifecycleDrill({ containerId, runId, sqlPort, sql });
  proxies.push(await createTenantProxy());
  start(
    process.execPath,
    ['node_modules/@angular/cli/bin/ng.js', 'serve', '--host', '127.0.0.1', '--port', '4200'],
    { NG_CLI_ANALYTICS: 'false' },
    process.cwd() + '/frontend',
  );
} catch (error) {
  console.error(error.message);
  await cleanup(1);
}
