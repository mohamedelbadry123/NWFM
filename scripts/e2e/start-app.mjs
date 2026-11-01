import { spawn, spawnSync } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import http from 'node:http';
import net from 'node:net';
import { setTimeout as delay } from 'node:timers/promises';
import { runTenantLifecycleDrill } from './tenant-lifecycle-drill.mjs';

if (process.env.NWFM_E2E_ISOLATED !== '1')
  throw new Error('This launcher requires the isolated e2e config.');
const runId = randomUUID();
const containerName = 'nwfm-e2e-' + runId;
let containerId;
let stopping = false;
const children = [];
const password = 'E2e!' + randomBytes(24).toString('hex');
const signingKey = randomBytes(48).toString('base64');
const platformSigningKey = randomBytes(48).toString('base64');
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
function runToExit(command, args, env) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      env: { ...process.env, ...env },
      stdio: 'inherit',
      detached: true,
      windowsHide: true,
    });
    children.push(child);
    const timeout = setTimeout(() => {
      child.kill();
      reject(new Error(command + ' ' + args.join(' ') + ' timed out.'));
    }, 240_000);
    child.on('error', (error) => {
      clearTimeout(timeout);
      reject(error);
    });
    child.on('exit', (code) => {
      clearTimeout(timeout);
      if (code === 0) resolve();
      else
        reject(new Error(command + ' ' + args.join(' ') + ' failed with exit code ' + code + '.'));
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
/** A loopback request carrying an explicit Host, the only tenant selector these probes use. */
function hostRequest(port, host, path) {
  return new Promise((resolve) => {
    const request = http.request(
      { host: '127.0.0.1', port, path, method: 'GET', headers: { host }, timeout: 15_000 },
      (response) => {
        let text = '';
        response.on('data', (chunk) => (text += chunk));
        response.on('end', () => {
          let body = null;
          try {
            body = text ? JSON.parse(text) : null;
          } catch {
            body = text;
          }
          resolve({ status: response.statusCode, body });
        });
      },
    );
    request.on('timeout', () => request.destroy());
    request.on('error', () => resolve({ status: 0, body: null }));
    request.end();
  });
}
async function healthy(port, application) {
  const response = await hostRequest(port, 'localhost', '/health');
  return response.status === 200 && response.body?.application === application;
}
function stopProcess(child) {
  if (child.exitCode !== null || child.signalCode !== null) return Promise.resolve();
  const exited = new Promise((resolve) => child.once('exit', resolve));
  try {
    if (process.platform === 'win32')
      spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], {
        windowsHide: true,
        stdio: 'ignore',
      });
    else process.kill(-child.pid, 'SIGTERM');
  } catch {
    /* already exited */
  }
  return Promise.race([exited, delay(10_000)]);
}
/**
 * Starts a short-lived host whose misconfiguration must be refused, runs the checks against it and
 * stops it. Output stays in memory: refusals name connection details that must not reach artifacts.
 */
async function probe(label, dll, contentRoot, port, application, env, check) {
  const child = spawn(
    'dotnet',
    [dll, '--contentRoot', contentRoot, '--urls', 'http://127.0.0.1:' + port],
    { env: { ...process.env, ...env }, stdio: 'pipe', detached: true, windowsHide: true },
  );
  children.push(child);
  let output = '';
  for (const stream of [child.stdout, child.stderr])
    stream.on('data', (chunk) => {
      output = (output + chunk.toString()).slice(-1_000_000);
    });
  const logged = async (message) => {
    const deadline = Date.now() + 15_000;
    while (Date.now() < deadline && !output.includes(message)) await delay(250);
    if (!output.includes(message))
      throw new Error(label + ' did not log the expected refusal: ' + message);
  };
  try {
    await ready(async () => {
      if (child.exitCode !== null) throw new Error(label + ' exited before it was ready.');
      return healthy(port, application);
    }, label);
    await check(logged);
  } finally {
    await stopProcess(child);
    children.splice(children.indexOf(child), 1);
  }
}
async function expectRefusal(port, host, status, code) {
  const response = await hostRequest(port, host, '/api/app-context');
  if (response.status !== status || response.body?.error?.code !== code)
    throw new Error(
      `Expected ${status} ${code} for ${host}, got ${response.status} ${response.body?.error?.code ?? ''}.`,
    );
}
try {
  for (const port of [4200, 5081, 5180, 5181, 5091, 2525]) await freePort(port);
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
  for (const project of [
    'backend/src/NWFM.Platform.Api',
    'backend/src/NWFM.Api',
    'backend/tools/NWFM.TenantAdmin',
  ])
    await runToExit('dotnet', ['build', project, '--configuration', 'Release'], {});

  const connection = (database, user, secret) =>
    `Server=127.0.0.1,${sqlPort};Database=${database};User Id=${user};Password=${secret};TrustServerCertificate=True;Max Pool Size=20`;
  // Two workspaces on one tenant API: alpha owns localhost, beta is reached as beta.localhost.
  const tenants = [
    {
      suffix: 'A',
      key: 'alpha',
      tenantId: '10000000-0000-0000-0000-000000000001',
      hosts: ['localhost', '127.0.0.1'],
    },
    { suffix: 'B', key: 'beta', tenantId: '20000000-0000-0000-0000-000000000002', hosts: [] },
  ].map((tenant) => ({
    ...tenant,
    database: 'NWFM_E2E_' + tenant.suffix,
    requestUser: 'nwfm_request_' + tenant.suffix,
    requestPassword: 'E2e!' + randomBytes(24).toString('hex'),
  }));
  // The operator creates each configuration tenant's request login before the control plane's
  // first migration, which grants it exactly the runtime permissions and nothing else.
  for (const tenant of tenants) {
    sql(
      `CREATE DATABASE [${tenant.database}];
       CREATE LOGIN [${tenant.requestUser}] WITH PASSWORD=N'${tenant.requestPassword}', CHECK_POLICY=ON;
       GO
       USE [${tenant.database}];
       CREATE USER [${tenant.requestUser}] FOR LOGIN [${tenant.requestUser}];`,
      'master',
    );
  }
  const callbackKey = randomBytes(32).toString('hex');
  const platformDll = 'backend/src/NWFM.Platform.Api/bin/Release/net10.0/NWFM.Platform.Api.dll';
  const platformRoot = process.cwd() + '/backend/src/NWFM.Platform.Api';
  const configurationTenant = (index, tenant, connectionName) => ({
    [`Tenancy__ConfigurationTenants__${index}__Id`]: tenant.tenantId,
    [`Tenancy__ConfigurationTenants__${index}__Key`]: tenant.key,
    [`Tenancy__ConfigurationTenants__${index}__Name`]: 'Tenant ' + tenant.suffix,
    [`Tenancy__ConfigurationTenants__${index}__ConnectionStringName`]: connectionName,
    [`Tenancy__ConfigurationTenants__${index}__RuntimePrincipal`]: tenant.requestUser,
    [`Tenancy__ConfigurationTenants__${index}__StoragePath`]:
      process.cwd() + '/.e2e/media/' + runId + '/' + tenant.suffix,
    [`Tenancy__ConfigurationTenants__${index}__ContactEmail`]: 'administrator@localhost',
    [`Tenancy__ConfigurationTenants__${index}__PlanCode`]: 'enterprise',
    [`Tenancy__ConfigurationTenants__${index}__SeedProfile`]: 'Bootstrap',
    ...Object.fromEntries(
      tenant.hosts.map((host, position) => [
        `Tenancy__ConfigurationTenants__${index}__Hosts__${position}`,
        host,
      ]),
    ),
  });
  // Not Development: nothing from the developer's appsettings (their local tenant) is inherited.
  const platformEnv = {
    ASPNETCORE_ENVIRONMENT: 'E2E',
    ConnectionStrings__CatalogConnection: connection('NWFM_Catalog', 'sa', password),
    ConnectionStrings__TenantA: connection('NWFM_E2E_A', 'sa', password),
    ConnectionStrings__TenantB: connection('NWFM_E2E_B', 'sa', password),
    Tenancy__RootDomain: 'localhost',
    ...configurationTenant(0, tenants[0], 'TenantA'),
    ...configurationTenant(1, tenants[1], 'TenantB'),
    Platform__AllowPrivateNetworkDatabaseHosts: 'true',
    Platform__BootstrapAdmin__Email: 'platform@localhost',
    Platform__BootstrapAdmin__Password: 'E2e!' + randomBytes(24).toString('hex'),
    PlatformJwt__SigningKey: platformSigningKey,
    Email__Enabled: 'true',
    Email__UseMock: 'true',
    Email__MockFolder: process.cwd() + '/.e2e/emails/' + runId,
    WorkflowDemo__Enabled: 'true',
    WorkflowDemo__HttpAddress: 'http://127.0.0.1:5091',
    WorkflowDemo__SmtpPort: '2525',
    WorkflowDemo__CallbackKey: callbackKey,
    WorkflowSettings__AllowPrivateConnections: 'true',
    CacheSettings__Provider: 'InMemory',
    Logging__LogLevel__Default: 'Warning',
  };
  start(
    'dotnet',
    [platformDll, '--contentRoot', platformRoot, '--urls', 'http://127.0.0.1:5180'],
    platformEnv,
  );
  // The port opens only after the catalog and both tenant databases are migrated and seeded.
  await ready(() => healthy(5180, 'NWFM.Platform'), 'NWFM control plane');
  // A workspace whose migration or seed failed is parked in maintenance; stop here, not in a probe.
  sql(
    `IF (SELECT COUNT(*) FROM catalog.Tenants WHERE Status=N'Active'
         AND Id IN (${tenants.map((tenant) => `'${tenant.tenantId}'`).join(',')}))<>${tenants.length}
     THROW 51221, 'The control plane did not activate every e2e workspace; see its log.', 1;`,
    'NWFM_Catalog',
  );

  for (const tenant of tenants) {
    sql(
      `IF (SELECT COUNT(*) FROM Tenancy.LifecycleAudit WHERE Action=N'Provisioned')<>1
         OR NOT EXISTS(SELECT 1 FROM Tenancy.LifecycleAudit
           WHERE Action=N'Provisioned' AND TenantId='${tenant.tenantId}' AND OperatorLogin=N'sa')
       THROW 51219, 'Provisioning must write exactly one attributed tenant lifecycle event.', 1;`,
      tenant.database,
    );
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
      tenant.database,
    );
  }

  // The control plane never adopts a database already bound to another workspace: a third
  // configuration tenant wired to alpha's database is refused and parked in maintenance.
  const gamma = {
    suffix: 'G',
    key: 'gamma',
    tenantId: randomUUID(),
    hosts: [],
    requestUser: 'nwfm_request_G',
  };
  await probe(
    'Foreign database adoption probe',
    platformDll,
    platformRoot,
    5181,
    'NWFM.Platform',
    {
      ...platformEnv,
      ConnectionStrings__TenantG: connection('NWFM_E2E_A', 'sa', password),
      ...configurationTenant(2, gamma, 'TenantG'),
    },
    (logged) => logged('Database belongs to another tenant.'),
  );
  sql(
    `IF NOT EXISTS(SELECT 1 FROM Tenancy.DeploymentBoundary WHERE TenantId='${tenants[0].tenantId}')
     THROW 51220, 'A refused adoption changed the tenant binding.', 1;`,
    'NWFM_E2E_A',
  );

  // The tenant API's catalog login reads the directory and the key ring, never owner credentials.
  const readerPassword = 'E2e!' + randomBytes(24).toString('hex');
  sql(
    readFileSync('scripts/provision-catalog-reader.sql', 'utf8').replaceAll(
      '$(NWFM_CATALOG_READER_PASSWORD)',
      readerPassword,
    ),
    'master',
  );
  const apiDll = 'backend/src/NWFM.Api/bin/Release/net10.0/NWFM.Api.dll';
  const apiRoot = process.cwd() + '/backend/src/NWFM.Api';
  const runtime = {
    ASPNETCORE_ENVIRONMENT: 'Development',
    ConnectionStrings__CatalogConnection: connection(
      'NWFM_Catalog',
      'nwfm_catalog_reader',
      readerPassword,
    ),
    ...Object.fromEntries(
      tenants.map((tenant) => [
        'ConnectionStrings__Tenant' + tenant.suffix,
        connection(tenant.database, tenant.requestUser, tenant.requestPassword),
      ]),
    ),
    JwtSettings__SigningKey: signingKey,
    CorsOrigins__0: 'http://localhost:4200',
    CorsOrigins__1: 'http://beta.localhost:4200',
    WorkflowDemo__HttpAddress: 'http://127.0.0.1:5091',
    WorkflowDemo__SmtpPort: '2525',
    WorkflowDemo__CallbackKey: callbackKey,
    WorkflowSettings__AllowPrivateConnections: 'true',
    // One API serves both workspaces; only beta's queue is kept small enough to exhaust.
    ['WorkflowSettings__MaxPendingPerQueueByTenant__' + tenants[1].tenantId]: '8',
    Sso__Enabled: 'false',
    ActiveDirectory__Enabled: 'false',
    TeamOtp__Enabled: 'false',
    C2m__Enabled: 'false',
    CacheSettings__Provider: 'InMemory',
    Logging__LogLevel__Default: 'Warning',
    'Logging__LogLevel__Microsoft.AspNetCore': 'Warning',
  };

  // A passing healthy deployment alone cannot prove these controls fail closed. Every workspace
  // whose request credential is not confined to its own bound database is refused per request.
  await probe(
    'Administrative and mis-wired credential probe',
    apiDll,
    apiRoot,
    5081,
    'NWFM',
    {
      ...runtime,
      ConnectionStrings__TenantA: connection('NWFM_E2E_A', 'sa', password),
      ConnectionStrings__TenantB: connection(
        'NWFM_E2E_A',
        tenants[0].requestUser,
        tenants[0].requestPassword,
      ),
    },
    async (logged) => {
      await expectRefusal(5081, 'localhost', 503, 'Tenant.Unverified');
      await logged('The request database identity has administrative privileges.');
      await expectRefusal(5081, 'beta.localhost', 503, 'Tenant.Unverified');
      await logged('Database tenant binding is missing or mismatched.');
    },
  );
  sql(
    `DENY VIEW ANY DATABASE TO [${tenants[1].requestUser}];
     GRANT IMPERSONATE ON LOGIN::[sa] TO [${tenants[0].requestUser}];`,
    'master',
  );
  await probe('Server privilege probe', apiDll, apiRoot, 5081, 'NWFM', runtime, async (logged) => {
    await expectRefusal(5081, 'localhost', 503, 'Tenant.Unverified');
    await logged('The request identity can impersonate another server login.');
    await expectRefusal(5081, 'beta.localhost', 503, 'Tenant.Unverified');
    await logged('The request identity cannot verify the database isolation inventory.');
  });
  sql(
    `REVOKE VIEW ANY DATABASE FROM [${tenants[1].requestUser}];
     REVOKE IMPERSONATE ON LOGIN::[sa] FROM [${tenants[0].requestUser}];`,
    'master',
  );

  start('dotnet', [apiDll, '--contentRoot', apiRoot, '--urls', 'http://127.0.0.1:5081'], runtime);
  await ready(() => healthy(5081, 'NWFM'), 'NWFM tenant API');
  for (const tenant of tenants) {
    const host = tenant.hosts[0] ?? tenant.key + '.localhost';
    const response = await hostRequest(5081, host, '/api/app-context');
    if (response.status !== 200 || response.body?.tenant?.id !== tenant.tenantId)
      throw new Error(`Host ${host} did not resolve to workspace ${tenant.key}.`);
  }
  // An unregistered subdomain names no workspace at all: refused, never served from a default.
  await expectRefusal(5081, 'unknown.localhost', 400, 'Tenant.Required');
  await expectRefusal(5081, 'gamma.localhost', 503, 'Tenant.Maintenance');

  for (const [own, other] of [tenants, [...tenants].reverse()]) {
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
  // beta.localhost:4200 reaches the same dev server; its proxy keeps the Host for the API.
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
