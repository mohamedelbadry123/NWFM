import { spawn, spawnSync } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import net from 'node:net';
import { setTimeout as delay } from 'node:timers/promises';

if (process.env.NWFM_E2E_ISOLATED !== '1')
  throw new Error('This launcher requires the isolated e2e config.');
const runId = randomUUID();
const containerName = 'nwfm-e2e-' + runId;
let containerId;
let stopping = false;
const children = [];
const password = 'E2e!' + randomBytes(24).toString('hex');
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
    if (child.exitCode === null) {
      try {
        process.kill(-child.pid, 'SIGTERM');
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
function start(command, args, env = {}) {
  const child = spawn(command, args, {
    env: { ...process.env, ...env },
    stdio: 'inherit',
    detached: true,
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
  for (const port of [4200, 5081, 5091, 2525]) await freePort(port);
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
  start('node', ['scripts/workflow-demo-transports.mjs']);
  start(
    'dotnet',
    [
      'run',
      '--project',
      'backend/src/NWFM.Api',
      '--no-launch-profile',
      '--urls',
      'http://127.0.0.1:5081',
    ],
    {
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__DefaultConnection:
        'Server=127.0.0.1,' +
        sqlPort +
        ';Database=NWFM_E2E;User Id=sa;Password=' +
        password +
        ';TrustServerCertificate=True',
      DatabaseStartup__ApplyMigrations: 'true',
      DatabaseStartup__SeedData: 'true',
      DatabaseStartup__ApplySqlObjects: 'true',
      WorkflowDemo__Enabled: 'true',
      WorkflowDemo__CallbackKey: randomBytes(32).toString('hex'),
      WorkflowSettings__AllowPrivateConnections: 'true',
      Sso__Enabled: 'false',
      ActiveDirectory__Enabled: 'false',
      TeamOtp__Enabled: 'false',
      C2m__Enabled: 'false',
      FileStorage__Root: process.cwd() + '/.e2e/media',
      Logging__LogLevel__Default: 'Warning',
    },
  );
  await ready(async () => {
    try {
      const response = await fetch('http://127.0.0.1:5081/health', {
        signal: AbortSignal.timeout(2000),
      });
      return response.ok && (await response.json()).application === 'NWFM';
    } catch {
      return false;
    }
  }, 'NWFM API');
  start('npm', ['--prefix', 'frontend', 'start', '--', '--host', '127.0.0.1', '--port', '4200']);
} catch (error) {
  console.error(error.message);
  await cleanup(1);
}
